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
    private bool _showEasterEggs;
    private bool _workerElevated;
    private bool _batteryControlBusy;
    private bool _batteryStateKnown;
    private bool _batteryControlSupported;
    private string _batteryControlStatusMessage = ResourceStringService.GetString("Battery.Status.DefaultMessage", "Start the background service to view or change the current power status.");
    private int _switchToBatteryModeOnDcThresholdPercent = BatteryControlCatalog.AutoSwitchNeverThreshold;
    private string _currentPerformanceModeKey = BatteryControlCatalog.DefaultPerformanceModeKey;
    private string _currentPerformanceSelectionKey = BatteryControlCatalog.DefaultSelectedPerformanceModeKey;
    private bool _resetChargeLimitToFullOnStartup;
    private int _currentChargeLimitPercent = BatteryControlCatalog.DefaultChargeLimitPercent;
    private int _currentBatteryHealthPercent = -1;
    private int _osdDurationMs = RuntimeDefaults.DefaultOsdDurationMs;
    private string _osdDisplayMode = OsdDisplayModes.IconOnly;
    private int _osdBackgroundOpacityPercent = RuntimeDefaults.DefaultOsdBackgroundOpacityPercent;
    private int _osdSizePercent = 100;
    private bool _capsLockOsdEnabled = true;
    private int _touchpadLightPressThreshold = RuntimeDefaults.DefaultTouchpadLightPressThreshold;
    private int _touchpadLightPressReleaseThreshold = RuntimeDefaults.DefaultTouchpadLightPressReleaseThreshold;
    private int _touchpadDeepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold;
    private int _touchpadDeepPressReleaseThreshold = RuntimeDefaults.DefaultTouchpadDeepPressReleaseThreshold;
    private int _touchpadLongPressDurationMs = RuntimeDefaults.DefaultTouchpadCornerLongPressDurationMs;
    private int _touchpadFeedbackStrength = RuntimeDefaults.DefaultTouchpadFeedbackStrength;
    private int _touchpadDeepPressFeedbackStrength = RuntimeDefaults.DefaultTouchpadDeepPressFeedbackStrength;
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

    public ObservableCollection<PerformanceCycleModeItemViewModel> PerformanceCycleModeItems { get; } = [];

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

    public bool ShowEasterEggs
    {
        get => _showEasterEggs;
        private set => SetProperty(ref _showEasterEggs, value);
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

    public string CurrentPerformanceModeLabel => BatteryControlCatalog.GetSelectedPerformanceModeLabel(
        BatteryControlCatalog.ResolveSelectedPerformanceModeKey(CurrentPerformanceModeKey, CurrentPerformanceSelectionKey));

    public string CurrentPerformanceSelectionKey
    {
        get => _currentPerformanceSelectionKey;
        private set => SetProperty(ref _currentPerformanceSelectionKey, value);
    }

    public int SwitchToBatteryModeOnDcThresholdPercent
    {
        get => _switchToBatteryModeOnDcThresholdPercent;
        private set => SetProperty(ref _switchToBatteryModeOnDcThresholdPercent, value);
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

    public int CurrentBatteryHealthPercent
    {
        get => _currentBatteryHealthPercent;
        private set
        {
            if (SetProperty(ref _currentBatteryHealthPercent, value))
            {
                OnPropertyChanged(nameof(CurrentBatteryHealthLabel));
                OnPropertyChanged(nameof(CurrentBatteryHealthStatusLabel));
            }
        }
    }

    public string CurrentBatteryHealthLabel => CurrentBatteryHealthPercent < 0
        ? ResourceStringService.GetString("Battery.Health.Unavailable", "Unavailable")
        : CurrentBatteryHealthPercent.ToString(System.Globalization.CultureInfo.CurrentCulture) + "%";

    public string CurrentBatteryHealthStatusLabel => CurrentBatteryHealthPercent switch
    {
        >= 100 => ResourceStringService.GetString("Battery.Health.Excellent", "Excellent"),
        >= 80 => ResourceStringService.GetString("Battery.Health.Good", "Good"),
        >= 60 => ResourceStringService.GetString("Battery.Health.Fair", "Fair"),
        >= 0 => ResourceStringService.GetString("Battery.Health.Poor", "Poor"),
        _ => string.Empty
    };

    public bool ResetChargeLimitToFullOnStartup
    {
        get => _resetChargeLimitToFullOnStartup;
        private set => SetProperty(ref _resetChargeLimitToFullOnStartup, value);
    }

    public bool BatteryControlsEnabled => ServiceRunning && WorkerElevated && BatteryStateKnown && BatteryControlSupported && !BatteryControlBusy;

    public string ServiceStatusLabel => ServiceState switch
    {
        WorkerServiceState.Running => ResourceStringService.GetString("ServiceStatusShort.Running", "Running"),
        WorkerServiceState.Starting => ResourceStringService.GetString("ServiceStatusShort.Starting", "Starting"),
        WorkerServiceState.Stopping => ResourceStringService.GetString("ServiceStatusShort.Stopping", "Stopping"),
        WorkerServiceState.UnexpectedlyStopped => ResourceStringService.GetString("ServiceStatusShort.WorkerStopped", "Worker stopped"),
        _ => ResourceStringService.GetString("ServiceStatusShort.Stopped", "Stopped")
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

    public int OsdSizePercent
    {
        get => _osdSizePercent;
        private set => SetProperty(ref _osdSizePercent, value);
    }

    public bool CapsLockOsdEnabled
    {
        get => _capsLockOsdEnabled;
        private set => SetProperty(ref _capsLockOsdEnabled, value);
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

    public int TouchpadLightPressReleaseThreshold
    {
        get => _touchpadLightPressReleaseThreshold;
        private set => SetProperty(ref _touchpadLightPressReleaseThreshold, value);
    }

    public int TouchpadDeepPressThreshold
    {
        get => _touchpadDeepPressThreshold;
        private set => SetProperty(ref _touchpadDeepPressThreshold, value);
    }

    public int TouchpadDeepPressReleaseThreshold
    {
        get => _touchpadDeepPressReleaseThreshold;
        private set => SetProperty(ref _touchpadDeepPressReleaseThreshold, value);
    }

    public int TouchpadFeedbackStrength
    {
        get => _touchpadFeedbackStrength;
        private set => SetProperty(ref _touchpadFeedbackStrength, value);
    }

    public int TouchpadDeepPressFeedbackStrength
    {
        get => _touchpadDeepPressFeedbackStrength;
        private set => SetProperty(ref _touchpadDeepPressFeedbackStrength, value);
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
        BatteryControlStatusMessage = ResourceStringService.GetString("Battery.Status.Preparing", "Preparing battery controls...");

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
                    MarkWorkerState(WorkerServiceState.Stopped);
                    BatteryControlStatusMessage = ResourceStringService.GetString("Battery.Status.CouldNotStartElevated", "Could not start the elevated worker.");
                    return false;
                }

                SetServiceState(WorkerServiceState.Starting, false);
                _ = ObserveWorkerStartupAsync();
            }

            if (!await WaitForWorkerReadyAsync(requireElevated: true))
            {
                BatteryControlStatusMessage = ResourceStringService.GetString("Battery.Status.NotElevated", "The worker did not enter elevated mode.");
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
        BatteryControlStatusMessage = ResourceStringService.GetString("Battery.Status.Reading", "Reading battery controls...");

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
        var normalizedModeKey = BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(modeKey);
        var previousSelectionKey = CurrentPerformanceSelectionKey;

        CurrentPerformanceSelectionKey = normalizedModeKey;
        BatteryStateKnown = true;
        BatteryControlSupported = true;
        BatteryControlStatusMessage = ResourceStringService.GetString("Battery.Status.UpdatingPerformance", "Updating performance mode...");
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
                throw new InvalidOperationException(response?.Error ?? ResourceStringService.GetString("Battery.Status.CouldNotChangePerformance", "Could not change the performance mode."));
            }

            _configuration.Preferences.PreferredPerformanceModeKey = normalizedModeKey;
            SaveConfiguration();
            ApplyWorkerSnapshot(response.Status);
            BatteryControlStatusMessage = ResourceStringService.GetString("Battery.Status.PerformanceUpdated", "Performance mode updated.");
        }
        catch
        {
            CurrentPerformanceSelectionKey = previousSelectionKey;
            throw;
        }
        finally
        {
            BatteryControlBusy = false;
        }
    }

    public void SetSwitchToBatteryModeOnDcThresholdPercent(int percent)
    {
        var normalizedPercent = BatteryControlCatalog.NormalizeBatteryModeOnDcThresholdPercent(percent);
        _configuration.Preferences.SwitchToBatteryModeOnDcThresholdPercent = normalizedPercent;
        SwitchToBatteryModeOnDcThresholdPercent = normalizedPercent;
        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public void SetPerformanceCycleModeIncluded(string modeKey, bool included)
    {
        var item = FindPerformanceCycleModeItem(modeKey);
        if (item is null)
        {
            return;
        }

        if (!included && PerformanceCycleModeItems.Count(entry => entry.IsIncluded) <= 1 && item.IsIncluded)
        {
            ReloadPerformanceCycleModeItems();
            return;
        }

        item.IsIncluded = included;
        PersistPerformanceCycleModePreferences();
    }

    public async Task<bool> SetChargeLimitPercentAsync(int percent)
    {
        var normalizedPercent = BatteryControlCatalog.NormalizeChargeLimitPercent(percent);
        var previousPercent = CurrentChargeLimitPercent;
        if (normalizedPercent == previousPercent)
        {
            return true;
        }

        CurrentChargeLimitPercent = normalizedPercent;
        BatteryStateKnown = true;
        BatteryControlSupported = true;
        _configuration.Preferences.PreferredChargeLimitPercent = normalizedPercent;
        SaveConfiguration();

        try
        {
            var response = await _workerPipeClient.SendAsync(new WorkerRequest
            {
                Command = WorkerCommandType.SetChargeLimit,
                ChargeLimitPercent = normalizedPercent
            }, 2500);
            if (response?.Success != true)
            {
                throw new InvalidOperationException(response?.Error ?? ResourceStringService.GetString("Battery.Status.CouldNotChangeCharge", "Could not change the charge limit."));
            }

            ApplyWorkerSnapshot(response.Status);
            return true;
        }
        catch
        {
            CurrentChargeLimitPercent = previousPercent;
            _configuration.Preferences.PreferredChargeLimitPercent = previousPercent;
            SaveConfiguration();
            return false;
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
        MarkBatteryStateUnknown(ResourceStringService.GetString("Battery.Status.WaitingReport", "Waiting for the background service to report the current power status..."));
        await _serviceOperationGate.WaitAsync();
        try
        {
            _serviceOperationInFlight = true;
            if (!await Task.Run(_workerProcessService.IsWorkerInstalled))
            {
                MarkWorkerState(WorkerServiceState.Stopped);
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
                        await WarmBatteryControlStateAsync();
                        return true;
                    }
                }

                await TryStopWorkerProcessAsync();

                ResetTouchpadLiveState();
            }

            if (!await Task.Run(_workerProcessService.StartWorker))
            {
                MarkWorkerState(WorkerServiceState.Stopped);
                return false;
            }

            if (!_touchpadStreamConnected)
            {
                TouchpadLive.Update(null, serviceAvailable: true, Touchpad.DeepPressThreshold);
            }

            if (!await WaitForWorkerReadyAsync(requireElevated: true))
            {
                BatteryControlStatusMessage = ResourceStringService.GetString("Battery.Status.NotElevated", "The worker did not enter elevated mode.");
                return false;
            }

            await WarmBatteryControlStateAsync();
            return true;
        }
        catch
        {
            MarkWorkerState(WorkerServiceState.Stopped);
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
        MarkBatteryStateUnknown(ResourceStringService.GetString("Battery.Status.Stopping", "The background service is stopping..."));
        await _serviceOperationGate.WaitAsync();
        try
        {
            _serviceOperationInFlight = true;
            var workerExited = await TryStopWorkerProcessAsync();

            if (workerExited)
            {
                MarkWorkerState(WorkerServiceState.Stopped);
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

    public void SetEasterEggsVisible(bool visible)
    {
        _configuration.Preferences.ShowEasterEggs = visible;
        ShowEasterEggs = visible;
        SaveConfiguration();
    }

    public void SetLanguagePreference(string languagePreference)
    {
        var normalizedPreference = AppLanguageService.ResolveStoredPreference(languagePreference);
        _configuration.Preferences.Language = normalizedPreference;
        LanguagePreference = normalizedPreference;
        SaveConfiguration();
    }

    public void ApplyOsdPreferences(int durationMs, string? displayMode, int backgroundOpacityPercent, int sizePercent)
    {
        _configuration.Preferences.Osd.DurationMs = OsdPreferences.NormalizeDuration(durationMs);
        _configuration.Preferences.Osd.DisplayMode = displayMode switch
        {
            OsdDisplayModes.IconOnly => OsdDisplayModes.IconOnly,
            OsdDisplayModes.TextOnly => OsdDisplayModes.TextOnly,
            _ => OsdDisplayModes.IconAndText
        };
        _configuration.Preferences.Osd.BackgroundOpacityPercent = OsdPreferences.NormalizeOpacity(backgroundOpacityPercent);
        _configuration.Preferences.Osd.SizePercent = OsdPreferences.NormalizeSize(sizePercent);

        SyncOsdPreferenceState();
        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public void SetCapsLockOsdEnabled(bool enabled)
    {
        _configuration.Preferences.Osd.ShowCapsLock = enabled;
        CapsLockOsdEnabled = enabled;
        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public void ApplyTouchpadPreferences(
        int lightPressThreshold,
        int lightPressReleaseThreshold,
        int deepPressThreshold,
        int deepPressReleaseThreshold,
        int longPressDurationMs)
    {
        var thresholds = TouchpadHardwareSettings.NormalizeAutomaticPressThresholds(
            lightPressThreshold,
            deepPressThreshold);
        var normalizedValue = Math.Clamp(longPressDurationMs, 200, 3000);
        _configuration.Touchpad.LightPressThreshold = thresholds.LightStart;
        _configuration.Touchpad.LightPressReleaseThreshold = thresholds.LightRelease;
        _configuration.Touchpad.PressSensitivityLevel = TouchpadHardwareSettings.MapThresholdToPressSensitivityLevel(thresholds.LightStart);
        _configuration.Touchpad.DeepPressThreshold = thresholds.DeepStart;
        _configuration.Touchpad.DeepPressReleaseThreshold = thresholds.DeepRelease;
        _configuration.Touchpad.LongPressDurationMs = normalizedValue;
        Touchpad.LightPressThreshold = thresholds.LightStart;
        Touchpad.LightPressReleaseThreshold = thresholds.LightRelease;
        Touchpad.DeepPressThreshold = thresholds.DeepStart;
        Touchpad.DeepPressReleaseThreshold = thresholds.DeepRelease;
        TouchpadLightPressThreshold = thresholds.LightStart;
        TouchpadLightPressReleaseThreshold = thresholds.LightRelease;
        TouchpadDeepPressThreshold = thresholds.DeepStart;
        TouchpadDeepPressReleaseThreshold = thresholds.DeepRelease;
        Touchpad.LongPressDurationMs = normalizedValue;
        TouchpadLongPressDurationMs = normalizedValue;
        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public async Task SetTouchpadHardwareHapticAsync(bool enabled)
    {
        await ApplyTouchpadHardwareSettingAsync(
            () => TouchpadPrivateHidService.SetHaptic(enabled),
            () =>
            {
                _configuration.Touchpad.DeepPressHapticsEnabled = enabled;
                Touchpad.DeepPressHapticsEnabled = enabled;
                TouchpadDeepPressHapticsEnabled = enabled;
            });
    }

    public async Task SetTouchpadHardwareVibrationAsync(int normalStrength, int deepPressStrength)
    {
        var strengths = TouchpadHardwareSettings.NormalizeFeedbackStrengths(normalStrength, deepPressStrength);
        await ApplyTouchpadHardwareSettingAsync(
            () => TouchpadPrivateHidService.SetVibration(strengths.Normal, strengths.DeepPress),
            () =>
            {
                _configuration.Touchpad.FeedbackLevel = TouchpadHardwareSettings.MapFeedbackStrengthToLevel(strengths.Normal);
                _configuration.Touchpad.FeedbackStrength = strengths.Normal;
                _configuration.Touchpad.DeepPressFeedbackStrength = strengths.DeepPress;
                Touchpad.FeedbackLevel = _configuration.Touchpad.FeedbackLevel;
                Touchpad.FeedbackStrength = strengths.Normal;
                Touchpad.DeepPressFeedbackStrength = strengths.DeepPress;
                TouchpadFeedbackStrength = strengths.Normal;
                TouchpadDeepPressFeedbackStrength = strengths.DeepPress;
            });
    }

    public async Task SetTouchpadHardwarePressAsync(
        int lightPressThreshold,
        int lightPressReleaseThreshold,
        int deepPressThreshold,
        int deepPressReleaseThreshold)
    {
        var thresholds = TouchpadHardwareSettings.NormalizeAutomaticPressThresholds(
            lightPressThreshold,
            deepPressThreshold);
        await ApplyTouchpadHardwareSettingAsync(
            () => TouchpadPrivateHidService.SetPress(
                thresholds.LightStart,
                thresholds.LightRelease,
                thresholds.DeepStart,
                thresholds.DeepRelease),
            () =>
            {
                _configuration.Touchpad.PressSensitivityLevel = TouchpadHardwareSettings.MapThresholdToPressSensitivityLevel(thresholds.LightStart);
                _configuration.Touchpad.LightPressThreshold = thresholds.LightStart;
                _configuration.Touchpad.LightPressReleaseThreshold = thresholds.LightRelease;
                _configuration.Touchpad.DeepPressThreshold = thresholds.DeepStart;
                _configuration.Touchpad.DeepPressReleaseThreshold = thresholds.DeepRelease;
                Touchpad.PressSensitivityLevel = _configuration.Touchpad.PressSensitivityLevel;
                Touchpad.LightPressThreshold = thresholds.LightStart;
                Touchpad.LightPressReleaseThreshold = thresholds.LightRelease;
                Touchpad.DeepPressThreshold = thresholds.DeepStart;
                Touchpad.DeepPressReleaseThreshold = thresholds.DeepRelease;
                TouchpadLightPressThreshold = thresholds.LightStart;
                TouchpadLightPressReleaseThreshold = thresholds.LightRelease;
                TouchpadDeepPressThreshold = thresholds.DeepStart;
                TouchpadDeepPressReleaseThreshold = thresholds.DeepRelease;
            },
            reloadWorker: true);
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
        _configService.Save(_configuration);
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
            mapping.UpdateDisplay(key?.ListTitle ?? ResourceStringService.GetString("Mapping.UnknownKey", "Unknown key"));
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
            ShowEasterEggs = _configuration.Preferences.ShowEasterEggs;
            SwitchToBatteryModeOnDcThresholdPercent = BatteryControlCatalog.NormalizeBatteryModeOnDcThresholdPercent(
                _configuration.Preferences.SwitchToBatteryModeOnDcThresholdPercent);
            ResetChargeLimitToFullOnStartup = _configuration.Preferences.ResetChargeLimitToFullOnStartup;
            ReloadPerformanceCycleModeItems();
            Touchpad = new TouchpadConfigurationViewModel(_configuration.Touchpad);
            TouchpadLightPressThreshold = Touchpad.LightPressThreshold;
            TouchpadLightPressReleaseThreshold = Touchpad.LightPressReleaseThreshold;
            TouchpadDeepPressThreshold = Touchpad.DeepPressThreshold;
            TouchpadDeepPressReleaseThreshold = Touchpad.DeepPressReleaseThreshold;
            TouchpadLongPressDurationMs = Touchpad.LongPressDurationMs;
            TouchpadFeedbackStrength = Touchpad.FeedbackStrength;
            TouchpadDeepPressFeedbackStrength = Touchpad.DeepPressFeedbackStrength;
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
        OsdDurationMs = OsdPreferences.NormalizeDuration(osd.DurationMs);
        OsdDisplayMode = osd.DisplayMode switch
        {
            OsdDisplayModes.IconOnly => OsdDisplayModes.IconOnly,
            OsdDisplayModes.TextOnly => OsdDisplayModes.TextOnly,
            _ => OsdDisplayModes.IconAndText
        };
        OsdBackgroundOpacityPercent = OsdPreferences.NormalizeOpacity(osd.BackgroundOpacityPercent);
        OsdSizePercent = OsdPreferences.NormalizeSize(osd.SizePercent);
        CapsLockOsdEnabled = osd.ShowCapsLock;
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
                MarkWorkerState(WorkerServiceState.UnexpectedlyStopped);
            }
        }
        finally
        {
            _statusRefreshInFlight = false;
        }
    }

    private async Task<bool> WaitForWorkerReadyAsync(bool requireElevated = false)
    {
        var sawWorkerProcess = false;
        for (var attempt = 0; attempt < 40; attempt++)
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

            if (await Task.Run(_workerProcessService.IsWorkerProcessRunning))
            {
                sawWorkerProcess = true;
            }
            else if (sawWorkerProcess || ServiceState != WorkerServiceState.Starting)
            {
                MarkWorkerState(WorkerServiceState.Stopped);
                return false;
            }

            await Task.Delay(80);
        }

        if (!sawWorkerProcess && ServiceState == WorkerServiceState.Starting)
        {
            MarkWorkerState(WorkerServiceState.Stopped);
        }

        return false;
    }

    private async Task<bool> WaitForWorkerExitAsync()
    {
        return await PollUntilAsync(
            maxAttempts: 20,
            delayMs: 120,
            conditionAsync: async () => !await Task.Run(_workerProcessService.IsWorkerProcessRunning));
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
            MarkBatteryStateUnknown(response?.Error ?? ResourceStringService.GetString("Battery.Status.CouldNotRead", "Could not read the battery controls."));
            ApplyWorkerSnapshot(response?.Status);
            return false;
        }

        ApplyWorkerSnapshot(response.Status);
        ApplyBatteryState(response.Battery);
        if (response.Battery.Supported)
        {
            BatteryControlStatusMessage = ResourceStringService.GetString("Battery.Status.Connected", "Connected to low-level battery controls.");
            return true;
        }

        BatteryControlStatusMessage = ResourceStringService.GetString("Battery.Status.NotSupported", "This device does not expose the required battery controls.");
        return false;
    }

    private async Task WarmBatteryControlStateAsync()
    {
        try
        {
            await RefreshBatteryControlStateCoreAsync();
        }
        catch
        {
        }
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
                    MarkWorkerState(WorkerServiceState.Stopped);
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
                MarkWorkerState(WorkerServiceState.Stopped);
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

                MarkWorkerState(WorkerServiceState.Stopped);
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

        if (status.IsRunning && ServiceState != WorkerServiceState.Stopping)
        {
            SetServiceState(WorkerServiceState.Running, status.IsElevated);
        }
        else
        {
            WorkerElevated = status.IsElevated;
        }

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

    private void SetServiceState(WorkerServiceState state, bool workerElevated)
    {
        WorkerElevated = workerElevated;
        ServiceState = state;
    }

    private void MarkWorkerState(WorkerServiceState state)
    {
        MarkBatteryStateUnknown(BuildBatteryRuntimeUnavailableMessage(serviceRunning: false, workerElevated: false));
        SetServiceState(state, false);
        ResetTouchpadLiveState();
    }

    private void ApplyBatteryState(BatteryControlState state)
    {
        BatteryStateKnown = true;
        BatteryControlSupported = state.Supported;
        if (!string.IsNullOrWhiteSpace(state.PerformanceModeKey))
        {
            CurrentPerformanceModeKey = BatteryControlCatalog.NormalizePerformanceModeKey(state.PerformanceModeKey);
        }

        CurrentPerformanceSelectionKey = BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(state.SelectedPerformanceModeKey);
        if (string.Equals(CurrentPerformanceSelectionKey, BatteryControlCatalog.Battery, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(_configuration.Preferences.PreferredPerformanceModeKey),
                BatteryControlCatalog.Battery,
                StringComparison.OrdinalIgnoreCase))
            {
                _configuration.Preferences.PreferredPerformanceModeKey =
                    BatteryControlCatalog.GetDefaultPerformanceModeCycleKey(_configuration.Preferences.PerformanceModeCycleKeys);
            }
        }
        else
        {
            _configuration.Preferences.PreferredPerformanceModeKey = CurrentPerformanceSelectionKey;
        }

        CurrentChargeLimitPercent = BatteryControlCatalog.NormalizeChargeLimitPercent(state.ChargeLimitPercent);
        CurrentBatteryHealthPercent = state.BatteryHealthPercent;
    }

    private void ReloadPerformanceCycleModeItems()
    {
        var includedKeys = BatteryControlCatalog.NormalizePerformanceModeCycleKeys(_configuration.Preferences.PerformanceModeCycleKeys);
        var includedKeySet = includedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orderedKeys = includedKeys
            .Concat(BatteryControlCatalog.PerformanceModeCycleOptionOrder.Where(key => !includedKeySet.Contains(key)))
            .ToList();

        PerformanceCycleModeItems.Clear();
        foreach (var modeKey in orderedKeys)
        {
            PerformanceCycleModeItems.Add(new PerformanceCycleModeItemViewModel(modeKey)
            {
                IsIncluded = includedKeySet.Contains(modeKey)
            });
        }

        SyncPerformanceCycleModeItemState();
    }

    private void PersistPerformanceCycleModePreferences()
    {
        _configuration.Preferences.PerformanceModeCycleKeys = PerformanceCycleModeItems
            .Where(item => item.IsIncluded)
            .Select(item => item.Key)
            .ToList();
        SyncPerformanceCycleModeItemState();
        SaveConfiguration();
        _ = ReloadWorkerAsync();
    }

    public void SavePerformanceCycleModeOrder()
    {
        PersistPerformanceCycleModePreferences();
    }

    public void MovePerformanceCycleModeUp(string modeKey)
    {
        var item = FindPerformanceCycleModeItem(modeKey);
        if (item is null)
        {
            return;
        }

        var index = PerformanceCycleModeItems.IndexOf(item);
        if (index <= 0)
        {
            return;
        }

        PerformanceCycleModeItems.Move(index, index - 1);
        PersistPerformanceCycleModePreferences();
    }

    public void MovePerformanceCycleModeDown(string modeKey)
    {
        var item = FindPerformanceCycleModeItem(modeKey);
        if (item is null)
        {
            return;
        }

        var index = PerformanceCycleModeItems.IndexOf(item);
        if (index < 0 || index >= PerformanceCycleModeItems.Count - 1)
        {
            return;
        }

        PerformanceCycleModeItems.Move(index, index + 1);
        PersistPerformanceCycleModePreferences();
    }

    private void SyncPerformanceCycleModeItemState()
    {
        var includedCount = PerformanceCycleModeItems.Count(item => item.IsIncluded);
        for (var index = 0; index < PerformanceCycleModeItems.Count; index++)
        {
            var item = PerformanceCycleModeItems[index];
            item.CanChangeInclusion = !item.IsIncluded || includedCount > 1;
            item.CanMoveUp = index > 0;
            item.CanMoveDown = index < PerformanceCycleModeItems.Count - 1;
        }
    }

    private PerformanceCycleModeItemViewModel? FindPerformanceCycleModeItem(string? modeKey)
    {
        var normalizedModeKey = BatteryControlCatalog.NormalizePerformanceModeCycleKey(modeKey);
        return PerformanceCycleModeItems.FirstOrDefault(item => string.Equals(item.Key, normalizedModeKey, StringComparison.OrdinalIgnoreCase));
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
            return ResourceStringService.GetString("Battery.Status.DefaultMessage", "Start the background service to view or change the current power status.");
        }

        if (!isElevated)
        {
            return ResourceStringService.GetString("Battery.Status.NeedAdmin", "Restart the background service with admin rights to view or change the current power status.");
        }

        return ResourceStringService.GetString("Battery.Status.WaitingReport", "Waiting for the background service to report the current power status...");
    }

    private async Task UpdateBatteryRuntimeAsync<T>(
        T nextValue,
        T previousValue,
        Action<T> applyCurrentValue,
        string updatingMessage,
        WorkerRequest request,
        string failureMessage,
        string successMessage,
        Action persistPreference)
    {
        applyCurrentValue(nextValue);
        BatteryStateKnown = true;
        BatteryControlSupported = true;
        BatteryControlStatusMessage = updatingMessage;
        BatteryControlBusy = true;
        try
        {
            var response = await _workerPipeClient.SendAsync(request, 2500);
            if (response?.Success != true)
            {
                throw new InvalidOperationException(response?.Error ?? failureMessage);
            }

            persistPreference();
            SaveConfiguration();
            ApplyWorkerSnapshot(response.Status);
            BatteryControlStatusMessage = successMessage;
        }
        catch
        {
            applyCurrentValue(previousValue);
            throw;
        }
        finally
        {
            BatteryControlBusy = false;
        }
    }

    private async Task ApplyTouchpadHardwareSettingAsync(Action hardwareAction, Action applyState, bool reloadWorker = false)
    {
        applyState();
        SaveConfiguration();

        if (reloadWorker)
        {
            _ = ReloadWorkerAsync();
        }

        await Task.Run(hardwareAction);
    }

    private async Task<bool> TryStopWorkerProcessAsync()
    {
        await _workerPipeClient.SendAsync(new WorkerRequest
        {
            Command = WorkerCommandType.StopWorker
        }, 600);

        if (await WaitForWorkerExitAsync())
        {
            return true;
        }

        await Task.Run(_workerProcessService.StopWorker);
        return await WaitForWorkerExitAsync();
    }

    private static async Task<bool> PollUntilAsync(int maxAttempts, int delayMs, Func<Task<bool>> conditionAsync)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (await conditionAsync())
            {
                return true;
            }

            await Task.Delay(delayMs);
        }

        return false;
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

        RefreshMappingReferences();
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
                MarkWorkerState(WorkerServiceState.UnexpectedlyStopped);
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
