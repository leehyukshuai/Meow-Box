using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using MeowBox.Core.Contracts;
using MeowBox.Core.Models;
using MeowBox.Core.Services;
using MeowBox.Controller.ViewModels;
using OsdDisplayModes = MeowBox.Core.Models.OsdDisplayMode;

namespace MeowBox.Controller.Services;

public sealed class MeowBoxController : ObservableObject, IDisposable
{
    private readonly AppConfigService _configService = new();
    private readonly InstalledAppService _installedAppService = new();
    private readonly ControllerPipeServer _controllerPipeServer;
    private readonly WorkerPipeClient _workerPipeClient = new();
    private readonly WorkerProcessService _workerProcessService = new();
    private readonly TouchpadStreamClient _touchpadStreamClient = new();
    private readonly SemaphoreSlim _serviceOperationGate = new(1, 1);

    private DispatcherQueue? _dispatcherQueue;
    private DispatcherQueueTimer? _statusTimer;
    private AppConfiguration _configuration = AppConfiguration.CreateDefault();
    private MappingDefinitionViewModel? _selectedMapping;
    private ActionTagOption? _selectedActionTag;
    private TouchpadConfigurationViewModel? _touchpad;
    private bool _touchpadMonitoringActive;
    private bool _touchpadStreamConnected;
    private bool _statusRefreshInFlight;
    private bool _serviceOperationInFlight;
    private WorkerServiceState _serviceState;
    private bool _isReloadingConfiguration;
    private string _languagePreference = AppLanguagePreference.System;
    private bool _trayIconEnabled;
    private bool _workerElevated;
    private bool _batteryControlBusy;
    private bool _batteryStateKnown;
    private bool _batteryControlSupported;
    private string _batteryControlStatusMessage = LocalizedText.Pick("Start the background service to view or change the current power status.", "请先启动后台服务，才能查看或修改当前电源状态。");
    private bool _resetPerformanceModeToSmartOnStartup = true;
    private string _currentPerformanceModeKey = BatteryControlCatalog.DefaultPerformanceModeKey;
    private bool _resetChargeLimitToFullOnStartup;
    private int _currentChargeLimitPercent = BatteryControlCatalog.DefaultChargeLimitPercent;
    private int _osdDurationMs = RuntimeDefaults.DefaultOsdDurationMs;
    private string _osdDisplayMode = OsdDisplayModes.IconOnly;
    private int _osdBackgroundOpacityPercent = RuntimeDefaults.DefaultOsdBackgroundOpacityPercent;
    private int _osdScalePercent = RuntimeDefaults.DefaultOsdScalePercent;
    private int _touchpadLightPressThreshold = RuntimeDefaults.DefaultTouchpadLightPressThreshold;
    private int _touchpadLongPressDurationMs = RuntimeDefaults.DefaultTouchpadCornerLongPressDurationMs;
    private int _touchpadPressSensitivityLevel = TouchpadHardwareSettings.Medium;
    private int _touchpadFeedbackLevel = TouchpadHardwareSettings.Medium;
    private bool _touchpadDeepPressHapticsEnabled = true;
    private bool _touchpadEdgeSlideEnabled;

    public MeowBoxController()
    {
        _controllerPipeServer = new ControllerPipeServer(OnWorkerNotificationAsync);
    }

    public ObservableCollection<KeyDefinitionViewModel> KeyItems { get; } = [];

    public ObservableCollection<MappingDefinitionViewModel> MappingItems { get; } = [];

    public ObservableCollection<ActionOptionItemViewModel> FilteredActionOptions { get; } = [];

    public TouchpadLiveStateViewModel TouchpadLive { get; } = new();

    public IReadOnlyList<ActionTagOption> ActionTags => ActionCatalog.TagOptions;

    public TouchpadConfigurationViewModel Touchpad
    {
        get => _touchpad ??= new TouchpadConfigurationViewModel(_configuration.Touchpad);
        private set => SetProperty(ref _touchpad, value);
    }

    public MappingDefinitionViewModel? SelectedMapping
    {
        get => _selectedMapping;
        set
        {
            if (SetProperty(ref _selectedMapping, value))
            {
                SyncSelectedActionTag();
                OnPropertyChanged(nameof(SelectedActionOption));
            }
        }
    }

    public ActionTagOption? SelectedActionTag
    {
        get => _selectedActionTag;
        set
        {
            if (SetProperty(ref _selectedActionTag, value))
            {
                RefreshFilteredActionOptions();
                OnPropertyChanged(nameof(SelectedActionOption));
            }
        }
    }

    public ActionOptionItemViewModel? SelectedActionOption
    {
        get => FilteredActionOptions.FirstOrDefault(item => item.IsSelected);
        set
        {
            if (SelectedMapping is null || value is null)
            {
                return;
            }

            ApplyActionType(value.Key);
        }
    }

    public WorkerServiceState ServiceState
    {
        get => _serviceState;
        private set
        {
            if (SetProperty(ref _serviceState, value))
            {
                OnPropertyChanged(nameof(ServiceRunning));
                OnPropertyChanged(nameof(ServiceStatusLabel));
                OnPropertyChanged(nameof(BatteryControlsEnabled));
            }
        }
    }

    public bool ServiceRunning => ServiceState == WorkerServiceState.Running;

    public bool IsReloadingConfiguration
    {
        get => _isReloadingConfiguration;
        private set => SetProperty(ref _isReloadingConfiguration, value);
    }

    public string LanguagePreference
    {
        get => _languagePreference;
        private set => SetProperty(ref _languagePreference, value);
    }

    public bool TrayIconEnabled
    {
        get => _trayIconEnabled;
        private set => SetProperty(ref _trayIconEnabled, value);
    }

    public bool WorkerElevated
    {
        get => _workerElevated;
        private set
        {
            if (SetProperty(ref _workerElevated, value))
            {
                OnPropertyChanged(nameof(BatteryControlsEnabled));
            }
        }
    }

    public bool BatteryControlBusy
    {
        get => _batteryControlBusy;
        private set
        {
            if (SetProperty(ref _batteryControlBusy, value))
            {
                OnPropertyChanged(nameof(BatteryControlsEnabled));
            }
        }
    }

    public bool BatteryStateKnown
    {
        get => _batteryStateKnown;
        private set
        {
            if (SetProperty(ref _batteryStateKnown, value))
            {
                OnPropertyChanged(nameof(BatteryControlsEnabled));
            }
        }
    }

    public bool BatteryControlSupported
    {
        get => _batteryControlSupported;
        private set
        {
            if (SetProperty(ref _batteryControlSupported, value))
            {
                OnPropertyChanged(nameof(BatteryControlsEnabled));
            }
        }
    }

    public string BatteryControlStatusMessage
    {
        get => _batteryControlStatusMessage;
        private set => SetProperty(ref _batteryControlStatusMessage, value);
    }

    public string CurrentPerformanceModeKey
    {
        get => _currentPerformanceModeKey;
        private set
        {
            if (SetProperty(ref _currentPerformanceModeKey, value))
            {
                OnPropertyChanged(nameof(CurrentPerformanceModeLabel));
            }
        }
    }

    public string CurrentPerformanceModeLabel => BatteryControlCatalog.GetPerformanceModeLabel(CurrentPerformanceModeKey);

    public bool ResetPerformanceModeToSmartOnStartup
    {
        get => _resetPerformanceModeToSmartOnStartup;
        private set => SetProperty(ref _resetPerformanceModeToSmartOnStartup, value);
    }

    public int CurrentChargeLimitPercent
    {
        get => _currentChargeLimitPercent;
        private set
        {
            if (SetProperty(ref _currentChargeLimitPercent, value))
            {
                OnPropertyChanged(nameof(CurrentChargeLimitLabel));
            }
        }
    }

    public string CurrentChargeLimitLabel => BatteryControlCatalog.GetChargeLimitLabel(CurrentChargeLimitPercent);

    public bool ResetChargeLimitToFullOnStartup
    {
        get => _resetChargeLimitToFullOnStartup;
        private set => SetProperty(ref _resetChargeLimitToFullOnStartup, value);
    }

    public bool BatteryControlsEnabled => ServiceRunning && WorkerElevated && BatteryStateKnown && BatteryControlSupported && !BatteryControlBusy;

    public string ServiceStatusLabel => ServiceState switch
    {
        WorkerServiceState.Running => LocalizedText.Pick("Running", "运行中"),
        WorkerServiceState.Starting => LocalizedText.Pick("Starting", "启动中"),
        WorkerServiceState.Stopping => LocalizedText.Pick("Stopping", "停止中"),
        WorkerServiceState.UnexpectedlyStopped => LocalizedText.Pick("Worker stopped", "后台已停止运行"),
        _ => LocalizedText.Pick("Stopped", "已停止")
    };

    public string ThemePreference => App.ThemeService.CurrentPreference;

    public string SupportedDeviceName => SupportedDeviceConfiguration.DeviceDisplayName;

    public string ConfigDirectory => _configService.ConfigDirectory;

    public string ConfigPath => _configService.ConfigPath;

    public int OsdDurationMs
    {
        get => _osdDurationMs;
        private set => SetProperty(ref _osdDurationMs, value);
    }

    public string OsdDisplayMode
    {
        get => _osdDisplayMode;
        private set => SetProperty(ref _osdDisplayMode, value);
    }

    public int OsdBackgroundOpacityPercent
    {
        get => _osdBackgroundOpacityPercent;
        private set => SetProperty(ref _osdBackgroundOpacityPercent, value);
    }

    public int OsdScalePercent
    {
        get => _osdScalePercent;
        private set => SetProperty(ref _osdScalePercent, value);
    }

    public int TouchpadLongPressDurationMs
    {
        get => _touchpadLongPressDurationMs;
        private set => SetProperty(ref _touchpadLongPressDurationMs, value);
    }

    public int TouchpadLightPressThreshold
    {
        get => _touchpadLightPressThreshold;
        private set => SetProperty(ref _touchpadLightPressThreshold, value);
    }

    public int TouchpadPressSensitivityLevel
    {
        get => _touchpadPressSensitivityLevel;
        private set => SetProperty(ref _touchpadPressSensitivityLevel, value);
    }

    public int TouchpadFeedbackLevel
    {
        get => _touchpadFeedbackLevel;
        private set => SetProperty(ref _touchpadFeedbackLevel, value);
    }

    public bool TouchpadDeepPressHapticsEnabled
    {
        get => _touchpadDeepPressHapticsEnabled;
        private set => SetProperty(ref _touchpadDeepPressHapticsEnabled, value);
    }

    public bool TouchpadEdgeSlideEnabled
    {
        get => _touchpadEdgeSlideEnabled;
        private set => SetProperty(ref _touchpadEdgeSlideEnabled, value);
    }

    public void Initialize(Window window)
    {
        _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();
        _configuration = _configService.Load();
        ReloadCollectionsFromConfiguration();
        _touchpadStreamClient.SnapshotReceived += OnTouchpadSnapshotReceived;
        _touchpadStreamClient.ConnectionChanged += OnTouchpadStreamConnectionChanged;

        App.ThemeService.Initialize(window, _configuration.Theme);
        StartStatusPolling();
        _ = RequestWorkerAnnouncementAsync();
    }

    public void SetTouchpadMonitoringActive(bool active)
    {
        if (_touchpadMonitoringActive == active)
        {
            return;
        }

        _touchpadMonitoringActive = active;
        if (active)
        {
            _touchpadStreamClient.Start();
        }
        else
        {
            _touchpadStreamClient.Stop();
            _touchpadStreamConnected = false;
        }

        _ = RefreshWorkerStatusAsync();
    }

    public void ApplyThemePreference(string theme)
    {
        _configuration.Theme = theme;
        App.ThemeService.ApplyPreference(theme);
        SaveConfiguration();
        OnPropertyChanged(nameof(ThemePreference));
    }

    public void SaveSelectedMapping()
    {
        if (IsReloadingConfiguration || SelectedMapping is null)
        {
            return;
        }

        RefreshMappingReferences();
        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public void SaveTouchpadConfiguration()
    {
        if (IsReloadingConfiguration)
        {
            return;
        }

        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public void ClearSelectedMappingAction()
    {
        if (SelectedMapping is null)
        {
            return;
        }

        SelectedMapping.Action.ClearAssignment();
        SelectedMapping.Enabled = false;
        RefreshMappingReferences();
    }

    public void ClearTouchpadAction()
    {
        Touchpad.DeepPressAction.ClearAssignment();
    }

    public async Task<IReadOnlyList<InstalledAppEntry>> GetInstalledAppsAsync()
    {
        return await _installedAppService.GetInstalledAppsAsync();
    }

    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public void OpenConfigFolder()
    {
        OpenFolder(ConfigDirectory);
    }

    public async Task<bool> EnsureBatteryControlReadyAsync()
    {
        BatteryControlBusy = true;
        BatteryControlStatusMessage = LocalizedText.Pick("Preparing battery controls...", "正在准备电池控制……");

        try
        {
            var statusResponse = await QueryWorkerStatusAsync(350);
            if (statusResponse?.Success == true && statusResponse.Status is not null)
            {
                ApplyWorkerSnapshot(statusResponse.Status);
                if (!statusResponse.Status.IsElevated)
                {
                    await StopWorkerServiceAsync();
                }
            }

            _workerProcessService.EnsureElevatedRuntimeTask();

            if (!_workerProcessService.IsWorkerProcessRunning())
            {
                if (!_workerProcessService.StartWorker())
                {
                    MarkWorkerStopped();
                    BatteryControlStatusMessage = LocalizedText.Pick("Could not start the elevated worker.", "无法启动管理员后台服务。");
                    return false;
                }

                SetServiceState(WorkerServiceState.Starting, false);
                _ = ObserveWorkerStartupAsync();
            }

            if (!await WaitForWorkerReadyAsync(requireElevated: true))
            {
                BatteryControlStatusMessage = LocalizedText.Pick("The worker did not enter elevated mode.", "后台服务未进入管理员模式。");
                return false;
            }

            return await RefreshBatteryControlStateCoreAsync();
        }
        catch (Exception exception)
        {
            BatteryControlStatusMessage = exception.Message;
            return false;
        }
        finally
        {
            BatteryControlBusy = false;
        }
    }

    public async Task<bool> RefreshBatteryControlStateAsync()
    {
        BatteryControlBusy = true;
        BatteryControlStatusMessage = LocalizedText.Pick("Reading battery controls...", "正在读取电池控制……");

        try
        {
            return await RefreshBatteryControlStateCoreAsync();
        }
        finally
        {
            BatteryControlBusy = false;
        }
    }

    public async Task SetPerformanceModeAsync(string modeKey)
    {
        var normalizedModeKey = BatteryControlCatalog.NormalizePerformanceModeKey(modeKey);
        var previousModeKey = CurrentPerformanceModeKey;
        CurrentPerformanceModeKey = normalizedModeKey;
        BatteryStateKnown = true;
        BatteryControlSupported = true;
        BatteryControlStatusMessage = LocalizedText.Pick("Updating performance mode...", "正在切换性能模式……");
        BatteryControlBusy = true;
        try
        {
            var response = await _workerPipeClient.SendAsync(new WorkerRequest
            {
                Command = WorkerCommandType.SetPerformanceMode,
                PerformanceModeKey = normalizedModeKey
            }, 2500);

            if (response?.Success != true)
            {
                throw new InvalidOperationException(response?.Error ?? LocalizedText.Pick("Could not change the performance mode.", "无法切换性能模式。"));
            }

            _configuration.Preferences.PreferredPerformanceModeKey = normalizedModeKey;
            SaveConfiguration();
            ApplyWorkerSnapshot(response.Status);
            BatteryControlStatusMessage = LocalizedText.Pick("Performance mode updated.", "性能模式已更新。");
        }
        catch
        {
            CurrentPerformanceModeKey = previousModeKey;
            throw;
        }
        finally
        {
            BatteryControlBusy = false;
        }
    }

    public void SetResetPerformanceModeToSmartOnStartup(bool enabled)
    {
        _configuration.Preferences.ResetPerformanceModeToSmartOnStartup = enabled;
        ResetPerformanceModeToSmartOnStartup = enabled;
        SaveConfiguration();
    }

    public async Task SetChargeLimitPercentAsync(int percent)
    {
        var normalizedPercent = BatteryControlCatalog.NormalizeChargeLimitPercent(percent);
        var previousPercent = CurrentChargeLimitPercent;
        CurrentChargeLimitPercent = normalizedPercent;
        BatteryStateKnown = true;
        BatteryControlSupported = true;
        BatteryControlStatusMessage = LocalizedText.Pick("Updating charge limit...", "正在切换充电限制……");
        BatteryControlBusy = true;
        try
        {
            var response = await _workerPipeClient.SendAsync(new WorkerRequest
            {
                Command = WorkerCommandType.SetChargeLimit,
                ChargeLimitPercent = normalizedPercent
            }, 2500);

            if (response?.Success != true)
            {
                throw new InvalidOperationException(response?.Error ?? LocalizedText.Pick("Could not change the charge limit.", "无法切换充电限制。"));
            }

            _configuration.Preferences.PreferredChargeLimitPercent = normalizedPercent;
            SaveConfiguration();
            ApplyWorkerSnapshot(response.Status);
            BatteryControlStatusMessage = LocalizedText.Pick("Charge limit updated.", "充电限制已更新。");
        }
        catch
        {
            CurrentChargeLimitPercent = previousPercent;
            throw;
        }
        finally
        {
            BatteryControlBusy = false;
        }
    }

    public void SetResetChargeLimitToFullOnStartup(bool enabled)
    {
        _configuration.Preferences.ResetChargeLimitToFullOnStartup = enabled;
        ResetChargeLimitToFullOnStartup = enabled;
        SaveConfiguration();
    }

    public async Task<bool> StartWorkerServiceAsync()
    {
        SetServiceState(WorkerServiceState.Starting, false);
        MarkBatteryStateUnknown(LocalizedText.Pick("Waiting for the background service to report the current power status...", "正在等待后台服务上报当前电源状态……"));
        await _serviceOperationGate.WaitAsync();
        try
        {
            _serviceOperationInFlight = true;
            if (!await Task.Run(_workerProcessService.IsWorkerInstalled))
            {
                MarkWorkerStopped();
                return false;
            }

            if (await Task.Run(_workerProcessService.IsWorkerProcessRunning))
            {
                var response = await QueryWorkerStatusAsync(300);
                if (response?.Success == true && response.Status is not null)
                {
                    ApplyWorkerSnapshot(response.Status);
                    if (response.Status.IsElevated)
                    {
                        await RequestWorkerAnnouncementAsync();
                        return true;
                    }
                }

                await _workerPipeClient.SendAsync(new WorkerRequest
                {
                    Command = WorkerCommandType.StopWorker
                }, 600);

                if (!await WaitForWorkerExitAsync())
                {
                    await Task.Run(_workerProcessService.StopWorker);
                    await WaitForWorkerExitAsync();
                }

                ResetTouchpadLiveState();
            }

            if (!await Task.Run(_workerProcessService.StartWorker))
            {
                MarkWorkerStopped();
                return false;
            }

            if (!_touchpadStreamConnected)
            {
                TouchpadLive.Update(null, serviceAvailable: true, Touchpad.DeepPressThreshold);
            }

            _ = ObserveWorkerStartupAsync();
            return true;
        }
        catch
        {
            MarkWorkerStopped();
            return false;
        }
        finally
        {
            _serviceOperationInFlight = false;
            _serviceOperationGate.Release();
        }
    }

    public async Task StopWorkerServiceAsync()
    {
        SetServiceState(WorkerServiceState.Stopping, false);
        MarkBatteryStateUnknown(LocalizedText.Pick("The background service is stopping...", "后台服务正在停止……"));
        await _serviceOperationGate.WaitAsync();
        try
        {
            _serviceOperationInFlight = true;
            await _workerPipeClient.SendAsync(new WorkerRequest
            {
                Command = WorkerCommandType.StopWorker
            }, 600);

            var workerExited = await WaitForWorkerExitAsync();
            if (!workerExited)
            {
                await Task.Run(_workerProcessService.StopWorker);
                workerExited = await WaitForWorkerExitAsync();
            }

            if (workerExited)
            {
                MarkWorkerStopped();
            }
            else
            {
                _ = ObserveWorkerShutdownAsync();
            }
        }
        finally
        {
            _serviceOperationInFlight = false;
            _serviceOperationGate.Release();
        }
    }

    public void SetTrayIconEnabled(bool enabled)
    {
        _configuration.Preferences.ShowTrayIcon = enabled;
        TrayIconEnabled = enabled;
        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public void SetLanguagePreference(string languagePreference)
    {
        var normalizedPreference = AppLanguageService.ResolveStoredPreference(languagePreference);
        _configuration.Preferences.Language = normalizedPreference;
        LanguagePreference = normalizedPreference;
        SaveConfiguration();
    }

    public void ApplyOsdPreferences(int durationMs, string? displayMode, int backgroundOpacityPercent, int scalePercent)
    {
        _configuration.Preferences.Osd.DurationMs = Math.Clamp(durationMs, 500, 10000);
        _configuration.Preferences.Osd.DisplayMode = displayMode switch
        {
            OsdDisplayModes.IconOnly => OsdDisplayModes.IconOnly,
            OsdDisplayModes.TextOnly => OsdDisplayModes.TextOnly,
            _ => OsdDisplayModes.IconAndText
        };
        _configuration.Preferences.Osd.BackgroundOpacityPercent = Math.Clamp(backgroundOpacityPercent, 0, 100);
        _configuration.Preferences.Osd.ScalePercent = Math.Clamp(scalePercent, 60, 200);

        SyncOsdPreferenceState();
        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public void ApplyTouchpadPreferences(int lightPressThreshold, int longPressDurationMs)
    {
        var normalizedLightPressThreshold = Math.Clamp(lightPressThreshold, 20, RuntimeDefaults.DefaultTouchpadDeepPressThreshold - 1);
        var normalizedValue = Math.Clamp(longPressDurationMs, 200, 3000);
        var normalizedPressSensitivityLevel = TouchpadHardwareSettings.MapThresholdToPressSensitivityLevel(normalizedLightPressThreshold);
        _configuration.Touchpad.LightPressThreshold = normalizedLightPressThreshold;
        _configuration.Touchpad.PressSensitivityLevel = normalizedPressSensitivityLevel;
        _configuration.Touchpad.LongPressDurationMs = normalizedValue;
        Touchpad.LightPressThreshold = normalizedLightPressThreshold;
        Touchpad.PressSensitivityLevel = normalizedPressSensitivityLevel;
        TouchpadLightPressThreshold = normalizedLightPressThreshold;
        TouchpadPressSensitivityLevel = normalizedPressSensitivityLevel;
        Touchpad.LongPressDurationMs = normalizedValue;
        TouchpadLongPressDurationMs = normalizedValue;
        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public async Task SetTouchpadHardwareHapticAsync(bool enabled)
    {
        await Task.Run(() => TouchpadPrivateHidService.SetHaptic(enabled));
        _configuration.Touchpad.DeepPressHapticsEnabled = enabled;
        Touchpad.DeepPressHapticsEnabled = enabled;
        TouchpadDeepPressHapticsEnabled = enabled;
        SaveConfiguration();
    }

    public async Task SetTouchpadHardwareVibrationAsync(int mode)
    {
        var normalizedMode = TouchpadHardwareSettings.NormalizeLevel(mode);
        await Task.Run(() => TouchpadPrivateHidService.SetVibration(normalizedMode));
        _configuration.Touchpad.FeedbackLevel = normalizedMode;
        Touchpad.FeedbackLevel = normalizedMode;
        TouchpadFeedbackLevel = normalizedMode;
        SaveConfiguration();
    }

    public async Task SetTouchpadHardwarePressAsync(int mode)
    {
        var normalizedMode = TouchpadHardwareSettings.NormalizeLevel(mode);
        var threshold = TouchpadHardwareSettings.MapPressSensitivityLevelToThreshold(normalizedMode);
        await Task.Run(() => TouchpadPrivateHidService.SetPress(normalizedMode));
        _configuration.Touchpad.PressSensitivityLevel = normalizedMode;
        _configuration.Touchpad.LightPressThreshold = threshold;
        Touchpad.PressSensitivityLevel = normalizedMode;
        Touchpad.LightPressThreshold = threshold;
        TouchpadPressSensitivityLevel = normalizedMode;
        TouchpadLightPressThreshold = threshold;
        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public void SetTouchpadEdgeSlideEnabled(bool enabled)
    {
        _configuration.Touchpad.EdgeSlideEnabled = enabled;
        if (enabled)
        {
            if (!Touchpad.LeftEdgeSlide.Action.HasAssignedAction)
            {
                Touchpad.LeftEdgeSlide.Action.Type = HotkeyActionType.BrightnessUp;
            }

            if (!Touchpad.RightEdgeSlide.Action.HasAssignedAction)
            {
                Touchpad.RightEdgeSlide.Action.Type = HotkeyActionType.VolumeUp;
            }
        }
        else
        {
            Touchpad.LeftEdgeSlide.Action.ClearAssignment();
            Touchpad.RightEdgeSlide.Action.ClearAssignment();
        }

        TouchpadEdgeSlideEnabled = Touchpad.EdgeSlideEnabled;
        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public async Task<bool> RestartWorkerServiceAsync()
    {
        if (_workerProcessService.IsWorkerProcessRunning())
        {
            await StopWorkerServiceAsync();
            await Task.Delay(250);
        }

        return await StartWorkerServiceAsync();
    }

    public void RestoreDefaults()
    {
        _configuration = _configService.RestoreDefaultFile();
        App.ThemeService.ApplyPreference(_configuration.Theme);
        ReloadCollectionsFromConfiguration();
        OnPropertyChanged(nameof(ThemePreference));
        _ = ReloadWorkerAsync();
    }

    public void RefreshMappingReferences()
    {
        var keyLookup = new Dictionary<string, KeyDefinitionViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in KeyItems)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || keyLookup.ContainsKey(item.Id))
            {
                continue;
            }

            keyLookup[item.Id] = item;
        }

        foreach (var mapping in MappingItems)
        {
            var key = keyLookup.TryGetValue(mapping.KeyId, out var keyVm)
                ? keyVm
                : null;
            mapping.UpdateDisplay(key?.ListTitle ?? LocalizedText.Pick("Unknown key", "未知按键"));
        }

        UpdateActionSelectionState();
        OnPropertyChanged(nameof(SelectedActionOption));
    }

    public void SetSelectedActionType(string type)
    {
        ApplyActionType(type);
    }

    public void SetTouchpadActionType(string type)
    {
        if (string.Equals(Touchpad.DeepPressAction.Type, type, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Touchpad.DeepPressAction.Type = type;
    }

    public void Dispose()
    {
        _statusTimer?.Stop();
        _controllerPipeServer.Dispose();
        _touchpadStreamClient.SnapshotReceived -= OnTouchpadSnapshotReceived;
        _touchpadStreamClient.ConnectionChanged -= OnTouchpadStreamConnectionChanged;
        _touchpadStreamClient.Dispose();
        _serviceOperationGate.Dispose();
    }

    private void SaveConfiguration()
    {
        if (IsReloadingConfiguration)
        {
            return;
        }

        _configuration.Theme = App.ThemeService.CurrentPreference;
        _configuration.Keys = KeyItems.Select(item => item.ToConfiguration()).ToList();
        _configuration.Mappings = MappingItems.Select(item => item.ToConfiguration()).ToList();
        _configuration.Touchpad = Touchpad.ToConfiguration();
        _configService.Save(_configuration);
    }

    private async Task ReloadWorkerAsync()
    {
        if (!ServiceRunning)
        {
            return;
        }

        await _workerPipeClient.SendAsync(new WorkerRequest
        {
            Command = WorkerCommandType.ReloadConfig
        }, 1500);

        await RefreshWorkerStatusAsync();
    }

    private void ReloadCollectionsFromConfiguration()
    {
        IsReloadingConfiguration = true;
        try
        {
            SelectedMapping = null;

            KeyItems.Clear();
            foreach (var key in _configuration.Keys)
            {
                KeyItems.Add(new KeyDefinitionViewModel(key));
            }

            MappingItems.Clear();
            foreach (var mapping in _configuration.Mappings)
            {
                MappingItems.Add(new MappingDefinitionViewModel(mapping));
            }

            SelectedActionTag = ActionTags.FirstOrDefault();
            LanguagePreference = _configuration.Preferences.Language;
            TrayIconEnabled = _configuration.Preferences.ShowTrayIcon;
            ResetPerformanceModeToSmartOnStartup = _configuration.Preferences.ResetPerformanceModeToSmartOnStartup;
            ResetChargeLimitToFullOnStartup = _configuration.Preferences.ResetChargeLimitToFullOnStartup;
            Touchpad = new TouchpadConfigurationViewModel(_configuration.Touchpad);
            TouchpadLightPressThreshold = Touchpad.LightPressThreshold;
            TouchpadLongPressDurationMs = Touchpad.LongPressDurationMs;
            TouchpadPressSensitivityLevel = Touchpad.PressSensitivityLevel;
            TouchpadFeedbackLevel = Touchpad.FeedbackLevel;
            TouchpadDeepPressHapticsEnabled = Touchpad.DeepPressHapticsEnabled;
            TouchpadEdgeSlideEnabled = Touchpad.EdgeSlideEnabled;
            SyncOsdPreferenceState();
            RefreshMappingReferences();
            SelectedMapping = MappingItems.FirstOrDefault();
            TouchpadLive.Update(null, serviceAvailable: false, Touchpad.DeepPressThreshold);
        }
        finally
        {
            IsReloadingConfiguration = false;
        }
    }

    private void SyncOsdPreferenceState()
    {
        var osd = _configuration.Preferences.Osd;
        OsdDurationMs = Math.Clamp(osd.DurationMs, 500, 10000);
        OsdDisplayMode = osd.DisplayMode switch
        {
            OsdDisplayModes.IconOnly => OsdDisplayModes.IconOnly,
            OsdDisplayModes.TextOnly => OsdDisplayModes.TextOnly,
            _ => OsdDisplayModes.IconAndText
        };
        OsdBackgroundOpacityPercent = Math.Clamp(osd.BackgroundOpacityPercent, 0, 100);
        OsdScalePercent = Math.Clamp(osd.ScalePercent, 60, 200);
    }

    private void StartStatusPolling()
    {
        _statusTimer?.Stop();
        _statusTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _statusTimer.IsRepeating = true;
        _statusTimer.Interval = TimeSpan.FromSeconds(5);
        _statusTimer.Tick += async (_, _) => await PollWorkerHeartbeatAsync();
        _statusTimer.Start();
    }

    private async Task<WorkerResponse?> QueryWorkerStatusAsync(int timeoutMs)
    {
        return await _workerPipeClient.SendAsync(new WorkerRequest
        {
            Command = WorkerCommandType.GetStatus
        }, timeoutMs);
    }

    private async Task RefreshWorkerStatusAsync()
    {
        if (_statusRefreshInFlight || _serviceOperationInFlight)
        {
            return;
        }

        _statusRefreshInFlight = true;

        try
        {
            var response = await QueryWorkerStatusAsync(350);
            if (response?.Success == true && response.Status is not null)
            {
                ApplyWorkerSnapshot(response.Status);
                return;
            }

            if (ServiceRunning)
            {
                MarkWorkerUnexpectedlyStopped();
            }
        }
        finally
        {
            _statusRefreshInFlight = false;
        }
    }

    private async Task<bool> WaitForWorkerReadyAsync(bool requireElevated = false)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await QueryWorkerStatusAsync(150);

            if (response?.Success == true && response.Status is not null)
            {
                ApplyWorkerSnapshot(response.Status);
                if (!requireElevated || response.Status.IsElevated)
                {
                    return true;
                }
            }

            if (!await Task.Run(_workerProcessService.IsWorkerProcessRunning))
            {
                MarkWorkerStopped();
                return false;
            }

            await Task.Delay(80);
        }

        return false;
    }

    private async Task<bool> WaitForWorkerExitAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (!await Task.Run(_workerProcessService.IsWorkerProcessRunning))
            {
                return true;
            }

            await Task.Delay(120);
        }

        return false;
    }

    private void ResetTouchpadLiveState()
    {
        _touchpadStreamConnected = false;
        TouchpadLive.Update(null, serviceAvailable: false, Touchpad.DeepPressThreshold);
    }

    private async Task<bool> RefreshBatteryControlStateCoreAsync()
    {
        var response = await _workerPipeClient.SendAsync(new WorkerRequest
        {
            Command = WorkerCommandType.GetBatteryControlState
        }, 2500);

        if (response?.Success != true || response.Battery is null)
        {
            MarkBatteryStateUnknown(response?.Error ?? LocalizedText.Pick("Could not read the battery controls.", "无法读取电池控制。"));
            ApplyWorkerSnapshot(response?.Status);
            return false;
        }

        ApplyWorkerSnapshot(response.Status);
        ApplyBatteryState(response.Battery);
        if (response.Battery.Supported)
        {
            BatteryControlStatusMessage = LocalizedText.Pick("Connected to low-level battery controls.", "已连接到底层电池控制。");
            return true;
        }

        BatteryControlStatusMessage = LocalizedText.Pick("This device does not expose the required battery controls.", "这台设备没有暴露所需的电池控制接口。");
        return false;
    }

    private async Task PollWorkerHeartbeatAsync()
    {
        if (!ServiceRunning)
        {
            return;
        }

        await RefreshWorkerStatusAsync();
    }

    private async Task RequestWorkerAnnouncementAsync()
    {
        var response = await _workerPipeClient.SendAsync(new WorkerRequest
        {
            Command = WorkerCommandType.AnnounceState
        }, 500);

        if (response?.Success == true && response.Status is not null)
        {
            ApplyWorkerSnapshot(response.Status);
        }
    }

    private async Task ObserveWorkerStartupAsync()
    {
        var attempt = 0;
        var startupDeadlineUtc = DateTime.UtcNow.AddSeconds(8);
        var sawWorkerProcess = false;
        var missingProcessChecks = 0;
        while (ServiceState == WorkerServiceState.Starting)
        {
            if (await Task.Run(_workerProcessService.IsWorkerProcessRunning))
            {
                sawWorkerProcess = true;
                missingProcessChecks = 0;
            }
            else
            {
                missingProcessChecks++;
                var startupFailed = sawWorkerProcess
                    ? missingProcessChecks >= 3
                    : DateTime.UtcNow >= startupDeadlineUtc;

                if (startupFailed)
                {
                    MarkWorkerStopped();
                    return;
                }
            }

            attempt++;
            if (attempt % 4 == 0)
            {
                await RequestWorkerAnnouncementAsync();
            }

            await Task.Delay(250);
        }
    }

    private async Task ObserveWorkerShutdownAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (ServiceState != WorkerServiceState.Stopping)
            {
                return;
            }

            if (!await Task.Run(_workerProcessService.IsWorkerProcessRunning))
            {
                MarkWorkerStopped();
                return;
            }

            await Task.Delay(120);
        }
    }

    private Task OnWorkerNotificationAsync(WorkerNotification notification)
    {
        if (_dispatcherQueue is null)
        {
            ApplyWorkerNotification(notification);
            return Task.CompletedTask;
        }

        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                ApplyWorkerNotification(notification);
                completionSource.SetResult();
            }
            catch (Exception exception)
            {
                completionSource.SetException(exception);
            }
        });

        return completionSource.Task;
    }

    private void ApplyWorkerNotification(WorkerNotification notification)
    {
        switch (notification.Type)
        {
            case WorkerNotificationType.Started:
                if (notification.Status is not null)
                {
                    SetServiceState(WorkerServiceState.Running, notification.Status.IsElevated);
                    ApplyWorkerSnapshot(notification.Status);
                }
                else
                {
                    SetServiceState(WorkerServiceState.Running, WorkerElevated);
                }
                break;
            case WorkerNotificationType.Stopped:
                if (ServiceState == WorkerServiceState.Starting)
                {
                    break;
                }

                MarkWorkerStopped();
                break;
        }
    }

    private void ApplyWorkerSnapshot(WorkerStatus? status)
    {
        if (status is null)
        {
            WorkerElevated = false;
            MarkBatteryStateUnknown(BuildBatteryRuntimeUnavailableMessage());
            if (!_touchpadStreamConnected)
            {
                TouchpadLive.Update(null, serviceAvailable: false, Touchpad.DeepPressThreshold);
            }

            return;
        }

        WorkerElevated = status.IsElevated;
        if (status.Battery is not null)
        {
            ApplyBatteryState(status.Battery);
        }
        else if (!status.IsRunning || !status.IsElevated)
        {
            MarkBatteryStateUnknown(BuildBatteryRuntimeUnavailableMessage(status.IsRunning, status.IsElevated));
        }

        if (!_touchpadStreamConnected || !ServiceRunning)
        {
            TouchpadLive.Update(status.Touchpad, serviceAvailable: status.IsRunning, Touchpad.DeepPressThreshold);
        }
    }

    private void MarkWorkerStopped()
    {
        MarkBatteryStateUnknown(BuildBatteryRuntimeUnavailableMessage(serviceRunning: false, workerElevated: false));
        SetServiceState(WorkerServiceState.Stopped, false);
        ResetTouchpadLiveState();
    }

    private void MarkWorkerUnexpectedlyStopped()
    {
        MarkBatteryStateUnknown(BuildBatteryRuntimeUnavailableMessage(serviceRunning: false, workerElevated: false));
        SetServiceState(WorkerServiceState.UnexpectedlyStopped, false);
        ResetTouchpadLiveState();
    }

    private void SetServiceState(WorkerServiceState state, bool workerElevated)
    {
        WorkerElevated = workerElevated;
        ServiceState = state;
    }

    private void ApplyBatteryState(BatteryControlState state)
    {
        BatteryStateKnown = true;
        BatteryControlSupported = state.Supported;
        if (!string.IsNullOrWhiteSpace(state.PerformanceModeKey))
        {
            CurrentPerformanceModeKey = state.PerformanceModeKey;
        }

        CurrentChargeLimitPercent = BatteryControlCatalog.NormalizeChargeLimitPercent(state.ChargeLimitPercent);
    }

    private void MarkBatteryStateUnknown(string statusMessage)
    {
        BatteryStateKnown = false;
        BatteryControlSupported = false;
        BatteryControlStatusMessage = statusMessage;
    }

    private string BuildBatteryRuntimeUnavailableMessage(bool? serviceRunning = null, bool? workerElevated = null)
    {
        var isRunning = serviceRunning ?? ServiceRunning;
        var isElevated = workerElevated ?? WorkerElevated;

        if (!isRunning)
        {
            return LocalizedText.Pick("Start the background service to view or change the current power status.", "请先启动后台服务，才能查看或修改当前电源状态。");
        }

        if (!isElevated)
        {
            return LocalizedText.Pick("Restart the background service with admin rights to view or change the current power status.", "请以管理员权限重新启动后台服务，才能查看或修改当前电源状态。");
        }

        return LocalizedText.Pick("Waiting for the background service to report the current power status...", "正在等待后台服务上报当前电源状态……");
    }

    private void ApplyActionType(string type)
    {
        if (SelectedMapping is null)
        {
            UpdateActionSelectionState();
            return;
        }

        if (string.Equals(SelectedMapping.Action.Type, type, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SelectedMapping.Action.Type = type;
        if (!string.IsNullOrWhiteSpace(type))
        {
            SelectedMapping.Enabled = true;
        }

        ApplyActionDefaults(SelectedMapping.Action);
        RefreshMappingReferences();
    }

    private void ApplyActionDefaults(ActionDefinitionViewModel action)
    {
    }

    private void OnTouchpadSnapshotReceived(object? sender, TouchpadLiveStateSnapshot snapshot)
    {
        if (_dispatcherQueue is null)
        {
            TouchpadLive.Update(snapshot, serviceAvailable: true, Touchpad.DeepPressThreshold);
            return;
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            TouchpadLive.Update(snapshot, serviceAvailable: true, Touchpad.DeepPressThreshold);
        });
    }

    private void OnTouchpadStreamConnectionChanged(object? sender, bool connected)
    {
        _touchpadStreamConnected = connected;
        if (connected)
        {
            return;
        }

        if (!_workerProcessService.IsWorkerProcessRunning())
        {
            if (ServiceRunning)
            {
                MarkWorkerUnexpectedlyStopped();
            }
            else
            {
                ResetTouchpadLiveState();
            }

            return;
        }

        _ = RefreshWorkerStatusAsync();
    }

    private void SyncSelectedActionTag()
    {
        if (SelectedMapping is null)
        {
            return;
        }

        var preferredTag = ActionCatalog.GetTags(SelectedMapping.Action.Type)
            .FirstOrDefault() ?? ActionTag.All;

        var option = ActionTags.FirstOrDefault(item => string.Equals(item.Key, preferredTag, StringComparison.OrdinalIgnoreCase))
            ?? ActionTags.First();

        if (!Equals(SelectedActionTag, option))
        {
            _selectedActionTag = option;
            OnPropertyChanged(nameof(SelectedActionTag));
            RefreshFilteredActionOptions();
        }
        else
        {
            UpdateActionSelectionState();
        }
    }

    private void RefreshFilteredActionOptions()
    {
        var tag = SelectedActionTag?.Key ?? ActionTag.All;
        FilteredActionOptions.Clear();
        foreach (var option in ActionCatalog.All.Where(item => ActionCatalog.MatchesTag(item, tag)))
        {
            FilteredActionOptions.Add(new ActionOptionItemViewModel(option));
        }

        UpdateActionSelectionState();
        OnPropertyChanged(nameof(SelectedActionOption));
    }

    private void UpdateActionSelectionState()
    {
        var selectedType = SelectedMapping?.Action.Type ?? string.Empty;
        foreach (var item in FilteredActionOptions)
        {
            item.IsSelected = string.Equals(item.Key, selectedType, StringComparison.OrdinalIgnoreCase);
        }
    }
}
