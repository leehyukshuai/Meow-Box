using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using FnMappingTool.Core.Contracts;
using FnMappingTool.Core.Models;
using FnMappingTool.Core.Services;
using FnMappingTool.Controller.ViewModels;
using OsdDisplayModes = FnMappingTool.Core.Models.OsdDisplayMode;

namespace FnMappingTool.Controller.Services;

public sealed class FnMappingToolController : ObservableObject, IDisposable
{
    private readonly AppConfigService _configService = new();
    private readonly InstalledAppService _installedAppService = new();
    private readonly AutostartService _autostartService = new();
    private readonly WorkerPipeClient _workerPipeClient = new();
    private readonly WorkerProcessService _workerProcessService = new();
    private readonly TouchpadStreamClient _touchpadStreamClient = new();

    private DispatcherQueue? _dispatcherQueue;
    private DispatcherQueueTimer? _statusTimer;
    private AppConfiguration _configuration = AppConfiguration.CreateDefault();
    private MappingDefinitionViewModel? _selectedMapping;
    private ActionTagOption? _selectedActionTag;
    private TouchpadConfigurationViewModel? _touchpad;
    private bool _touchpadMonitoringActive;
    private bool _touchpadStreamConnected;
    private bool _statusRefreshInFlight;
    private bool _serviceRunning;
    private bool _autostartEnabled;
    private bool _priorityStartupEnabled;
    private bool _priorityStartupBusy;
    private bool _isReloadingConfiguration;
    private string _languagePreference = AppLanguagePreference.System;
    private bool _trayIconEnabled;
    private int _osdDurationMs = RuntimeDefaults.DefaultOsdDurationMs;
    private string _osdDisplayMode = OsdDisplayModes.IconOnly;
    private int _osdBackgroundOpacityPercent = RuntimeDefaults.DefaultOsdBackgroundOpacityPercent;
    private int _osdScalePercent = RuntimeDefaults.DefaultOsdScalePercent;

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

    public bool ServiceRunning
    {
        get => _serviceRunning;
        private set
        {
            if (SetProperty(ref _serviceRunning, value))
            {
                OnPropertyChanged(nameof(ServiceStatusLabel));
            }
        }
    }

    public bool AutostartEnabled
    {
        get => _autostartEnabled;
        private set => SetProperty(ref _autostartEnabled, value);
    }

    public bool PriorityStartupEnabled
    {
        get => _priorityStartupEnabled;
        private set => SetProperty(ref _priorityStartupEnabled, value);
    }

    public bool PriorityStartupBusy
    {
        get => _priorityStartupBusy;
        private set => SetProperty(ref _priorityStartupBusy, value);
    }

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

    public string ServiceStatusLabel => ServiceRunning ? LocalizedText.Pick("Running", "运行中") : LocalizedText.Pick("Stopped", "已停止");

    public string ThemePreference => App.ThemeService.CurrentPreference;

    public string SupportedDeviceName => SupportedDeviceConfiguration.DeviceDisplayName;

    public string ConfigDirectory => _configService.ConfigDirectory;

    public string ConfigPath => _configService.ConfigPath;

    public string OsdIconDirectory => OsdIconPathResolver.GetOsdIconDirectory(_configService.ConfigDirectory);

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

    public void Initialize(Window window)
    {
        _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();
        SyncBundledOsdIconsToConfigDirectory();
        _configuration = _configService.Load();
        ReloadCollectionsFromConfiguration();
        _touchpadStreamClient.SnapshotReceived += OnTouchpadSnapshotReceived;
        _touchpadStreamClient.ConnectionChanged += OnTouchpadStreamConnectionChanged;

        App.ThemeService.Initialize(window, _configuration.Theme);
        _ = RefreshAutostartStateAsync();
        StartStatusPolling();
        _ = RefreshWorkerStatusAsync();
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
        SelectedMapping.Enabled = SelectedMapping.Osd.Enabled;
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

    public void OpenOsdIconFolder()
    {
        Directory.CreateDirectory(OsdIconDirectory);
        OpenFolder(OsdIconDirectory);
    }

    public void RefreshOsdIconCatalog()
    {
        SyncBundledOsdIconsToConfigDirectory();
    }

    public async Task<bool> StartWorkerServiceAsync()
    {
        if (!_workerProcessService.IsWorkerInstalled())
        {
            return false;
        }

        if (_workerProcessService.IsWorkerProcessRunning())
        {
            return await WaitForWorkerReadyAsync();
        }

        if (!_workerProcessService.StartWorker())
        {
            return false;
        }

        return await WaitForWorkerReadyAsync();
    }

    public async Task StopWorkerServiceAsync()
    {
        _ = await _workerPipeClient.SendAsync(new WorkerRequest
        {
            Command = WorkerCommandType.StopWorker
        }, 600);

        ResetTouchpadLiveState();

        for (var attempt = 0; attempt < 12; attempt++)
        {
            if (!_workerProcessService.IsWorkerProcessRunning())
            {
                await RefreshWorkerStatusAsync();
                return;
            }

            await Task.Delay(80);
        }

        await RefreshWorkerStatusAsync();
    }

    public void SetAutostart(bool enabled)
    {
        var workerExecutablePath = enabled
            ? _workerProcessService.ResolveWorkerExecutablePath()
            : _workerProcessService.WorkerExecutablePath;

        _autostartService.SetEnabled(enabled, workerExecutablePath ?? string.Empty, _configuration.Preferences.PreferPriorityStartup);
        RefreshAutostartState();
    }

    public async Task SetPriorityStartupEnabledAsync(bool enabled)
    {
        if (PriorityStartupBusy)
        {
            return;
        }

        var previousPreference = _configuration.Preferences.PreferPriorityStartup;
        PriorityStartupBusy = true;
        _configuration.Preferences.PreferPriorityStartup = enabled;
        PriorityStartupEnabled = enabled;
        SaveConfiguration();

        try
        {
            if (AutostartEnabled)
            {
                var workerExecutablePath = _workerProcessService.ResolveWorkerExecutablePath() ?? _workerProcessService.WorkerExecutablePath;
                await Task.Run(() => _autostartService.SetEnabled(true, workerExecutablePath, enabled));
            }

            RefreshAutostartState();
        }
        catch
        {
            _configuration.Preferences.PreferPriorityStartup = previousPreference;
            PriorityStartupEnabled = previousPreference;
            SaveConfiguration();
            RefreshAutostartState();
            throw;
        }
        finally
        {
            PriorityStartupBusy = false;
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

    public async Task<bool> RestartWorkerServiceAsync()
    {
        if (_workerProcessService.IsWorkerProcessRunning())
        {
            await StopWorkerServiceAsync();
            await Task.Delay(250);
        }

        return await StartWorkerServiceAsync();
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
        _touchpadStreamClient.SnapshotReceived -= OnTouchpadSnapshotReceived;
        _touchpadStreamClient.ConnectionChanged -= OnTouchpadStreamConnectionChanged;
        _touchpadStreamClient.Dispose();
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
            PriorityStartupEnabled = _configuration.Preferences.PreferPriorityStartup;
            LanguagePreference = _configuration.Preferences.Language;
            TrayIconEnabled = _configuration.Preferences.ShowTrayIcon;
            Touchpad = new TouchpadConfigurationViewModel(_configuration.Touchpad);
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

    private void SyncBundledOsdIconsToConfigDirectory()
    {
        Directory.CreateDirectory(OsdIconDirectory);

        var bundledDirectory = Path.Combine(AppContext.BaseDirectory, "assets", BuiltInAssetResolver.OsdIconsDirectoryName);
        if (!Directory.Exists(bundledDirectory))
        {
            return;
        }

        foreach (var sourcePath in Directory.GetFiles(bundledDirectory, "*.png", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            var destinationPath = Path.Combine(OsdIconDirectory, fileName);
            if (!File.Exists(destinationPath) || File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(destinationPath))
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }
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

    private void RefreshAutostartState()
    {
        var startupMode = _autostartService.GetStartupMode();
        ApplyAutostartState(startupMode);
    }

    private async Task RefreshAutostartStateAsync()
    {
        var startupMode = await Task.Run(_autostartService.GetStartupMode);

        if (_dispatcherQueue is null)
        {
            ApplyAutostartState(startupMode);
            return;
        }

        _dispatcherQueue.TryEnqueue(() => ApplyAutostartState(startupMode));
    }

    private void ApplyAutostartState(StartupRegistrationMode startupMode)
    {
        AutostartEnabled = startupMode != StartupRegistrationMode.Disabled;
        PriorityStartupEnabled = startupMode != StartupRegistrationMode.Disabled
            ? startupMode == StartupRegistrationMode.ScheduledTask
            : _configuration.Preferences.PreferPriorityStartup;
    }

    private void StartStatusPolling()
    {
        _statusTimer?.Stop();
        _statusTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _statusTimer.IsRepeating = true;
        _statusTimer.Interval = TimeSpan.FromSeconds(1.5);
        _statusTimer.Tick += async (_, _) => await RefreshWorkerStatusAsync();
        _statusTimer.Start();
    }

    private async Task RefreshWorkerStatusAsync()
    {
        if (_statusRefreshInFlight)
        {
            return;
        }

        _statusRefreshInFlight = true;

        try
        {
        var response = await _workerPipeClient.SendAsync(new WorkerRequest
        {
            Command = WorkerCommandType.GetStatus
        }, 350);

        ServiceRunning = response?.Success == true && response.Status is not null;
        if (!_touchpadStreamConnected || !ServiceRunning)
        {
            TouchpadLive.Update(ServiceRunning ? response?.Status?.Touchpad : null, ServiceRunning, Touchpad.DeepPressThreshold);
        }
        }
        finally
        {
            _statusRefreshInFlight = false;
        }
    }

    private async Task<bool> WaitForWorkerReadyAsync()
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var response = await _workerPipeClient.SendAsync(new WorkerRequest
            {
                Command = WorkerCommandType.GetStatus
            }, 150);

            if (response?.Success == true && response.Status is not null)
            {
                ServiceRunning = true;
                if (!_touchpadStreamConnected)
                {
                    TouchpadLive.Update(response.Status.Touchpad, serviceAvailable: true, Touchpad.DeepPressThreshold);
                }

                return true;
            }

            if (!_workerProcessService.IsWorkerProcessRunning())
            {
                ResetTouchpadLiveState();
                return false;
            }

            await Task.Delay(80);
        }

        await RefreshWorkerStatusAsync();
        return ServiceRunning;
    }

    private void ResetTouchpadLiveState()
    {
        _touchpadStreamConnected = false;
        ServiceRunning = false;
        TouchpadLive.Update(null, serviceAvailable: false, Touchpad.DeepPressThreshold);
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
            ServiceRunning = true;
            TouchpadLive.Update(snapshot, serviceAvailable: true, Touchpad.DeepPressThreshold);
            return;
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            ServiceRunning = true;
            TouchpadLive.Update(snapshot, serviceAvailable: true, Touchpad.DeepPressThreshold);
        });
    }

    private void OnTouchpadStreamConnectionChanged(object? sender, bool connected)
    {
        _touchpadStreamConnected = connected;
        if (connected)
        {
            if (_dispatcherQueue is null)
            {
                ServiceRunning = true;
            }
            else
            {
                _dispatcherQueue.TryEnqueue(() => ServiceRunning = true);
            }

            return;
        }

        if (!_workerProcessService.IsWorkerProcessRunning())
        {
            ResetTouchpadLiveState();
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
