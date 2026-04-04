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

    private DispatcherQueueTimer? _statusTimer;
    private AppConfiguration _configuration = AppConfiguration.CreateDefault();
    private KeyDefinitionViewModel? _selectedKey;
    private MappingDefinitionViewModel? _selectedMapping;
    private ActionTagOption? _selectedActionTag;
    private bool _serviceRunning;
    private bool _autostartEnabled;
    private bool _trayIconEnabled;
    private int _osdDurationMs = RuntimeDefaults.DefaultOsdDurationMs;
    private string _osdDisplayMode = OsdDisplayModes.IconOnly;
    private int _osdBackgroundOpacityPercent = RuntimeDefaults.DefaultOsdBackgroundOpacityPercent;
    private int _osdScalePercent = RuntimeDefaults.DefaultOsdScalePercent;

    public ObservableCollection<KeyDefinitionViewModel> KeyItems { get; } = [];

    public ObservableCollection<MappingDefinitionViewModel> MappingItems { get; } = [];

    public ObservableCollection<ActionOptionItemViewModel> FilteredActionOptions { get; } = [];

    public IReadOnlyList<ActionTagOption> ActionTags => ActionCatalog.TagOptions;

    public KeyDefinitionViewModel? SelectedKey
    {
        get => _selectedKey;
        set => SetProperty(ref _selectedKey, value);
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

    public bool TrayIconEnabled
    {
        get => _trayIconEnabled;
        private set => SetProperty(ref _trayIconEnabled, value);
    }

    public string ServiceStatusLabel => ServiceRunning ? "Running" : "Stopped";

    public string ThemePreference => App.ThemeService.CurrentPreference;

    public string ConfigDirectory => _configService.ConfigDirectory;

    public string ConfigPath => _configService.ConfigPath;

    public string PresetDirectory => Path.Combine(_configService.ConfigDirectory, "presets");

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
        SyncBundledOsdIconsToConfigDirectory();
        SyncBundledPresetsToConfigDirectory();
        _configuration = _configService.Load();
        ReloadCollectionsFromConfiguration();

        App.ThemeService.Initialize(window, _configuration.Theme);
        RefreshAutostartState();
        StartStatusPolling();
        _ = RefreshWorkerStatusAsync();
    }

    public void ApplyThemePreference(string theme)
    {
        _configuration.Theme = theme;
        App.ThemeService.ApplyPreference(theme);
        SaveConfiguration();
        OnPropertyChanged(nameof(ThemePreference));
    }

    public async Task<InputEvent?> CaptureNextEventAsync(CancellationToken cancellationToken = default)
    {
        if (!ServiceRunning)
        {
            return null;
        }

        var response = await _workerPipeClient.SendAsync(new WorkerRequest
        {
            Command = WorkerCommandType.CaptureNextEvent
        }, 30000, cancellationToken);

        return response?.Success == true ? response.CapturedEvent : null;
    }

    public void CancelCapture()
    {
    }

    public KeyDefinitionViewModel AddKey(string name, InputEvent inputEvent)
    {
        var normalizedName = NormalizeName(name, "New key");
        EnsureUniqueName(KeyItems.Select(item => item.ListTitle), normalizedName, "Key names must be unique.");

        var viewModel = new KeyDefinitionViewModel(new KeyDefinitionConfiguration
        {
            Name = normalizedName,
            Trigger = EventMatcherConfiguration.FromInputEvent(inputEvent)
        });

        KeyItems.Add(viewModel);
        SelectedKey = viewModel;
        SaveConfiguration();
        RefreshMappingReferences();
        _ = ReloadWorkerAsync();
        return viewModel;
    }

    public MappingDefinitionViewModel AddMapping()
    {
        var viewModel = new MappingDefinitionViewModel(new KeyActionMappingConfiguration
        {
            Enabled = true,
            KeyId = string.Empty,
            Action = new ActionDefinitionConfiguration
            {
                Type = HotkeyActionType.None
            }
        });

        MappingItems.Add(viewModel);
        SelectedMapping = viewModel;
        RefreshMappingReferences();
        SaveConfiguration();
        _ = ReloadWorkerAsync();
        return viewModel;
    }

    public void SaveSelectedKey()
    {
        if (SelectedKey is null)
        {
            return;
        }

        EnsureUniqueName(
            KeyItems.Where(item => item != SelectedKey).Select(item => item.ListTitle),
            NormalizeName(SelectedKey.Name, "Unnamed key"),
            "Key names must be unique.");

        SelectedKey.Name = NormalizeName(SelectedKey.Name, "Unnamed key");
        SaveConfiguration();
        RefreshMappingReferences();
        _ = ReloadWorkerAsync();
    }

    public int DeleteSelectedKey()
    {
        if (SelectedKey is null)
        {
            return 0;
        }

        var keyId = SelectedKey.Id;
        var removedMappings = MappingItems.Where(item => string.Equals(item.KeyId, keyId, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var mapping in removedMappings)
        {
            MappingItems.Remove(mapping);
        }

        KeyItems.Remove(SelectedKey);
        SelectedKey = KeyItems.FirstOrDefault();
        if (SelectedMapping is not null && removedMappings.Any(item => item.Id == SelectedMapping.Id))
        {
            SelectedMapping = MappingItems.FirstOrDefault();
        }

        SaveConfiguration();
        RefreshMappingReferences();
        _ = ReloadWorkerAsync();
        return removedMappings.Count;
    }

    public void SaveSelectedMapping()
    {
        if (SelectedMapping is null)
        {
            return;
        }

        SaveConfiguration();
        RefreshMappingReferences();
        _ = ReloadWorkerAsync();
    }

    public bool DeleteSelectedMapping()
    {
        if (SelectedMapping is null)
        {
            return false;
        }

        var mappingToDelete = SelectedMapping;
        MappingItems.Remove(mappingToDelete);
        SelectedMapping = MappingItems.FirstOrDefault();
        SaveConfiguration();
        RefreshMappingReferences();
        _ = ReloadWorkerAsync();
        return true;
    }

    public void ClearSelectedMappingAction()
    {
        if (SelectedMapping is null)
        {
            return;
        }

        SelectedMapping.Action.ClearAssignment();
        RefreshMappingReferences();
    }

    public bool IsDuplicateKeyName(string name, string? excludedId = null)
    {
        return IsDuplicateName(KeyItems.Select(item => (item.Id, item.ListTitle)), name, excludedId);
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

    public void OpenOsdIconFolder()
    {
        Directory.CreateDirectory(OsdIconDirectory);
        OpenFolder(OsdIconDirectory);
    }

    public void RefreshOsdIconCatalog()
    {
        SyncBundledOsdIconsToConfigDirectory();
    }

    public void RefreshPresetCatalog()
    {
        SyncBundledPresetsToConfigDirectory();
    }

    public async Task<bool> StartWorkerServiceAsync()
    {
        if (!_workerProcessService.IsWorkerInstalled())
        {
            return false;
        }

        if (_workerProcessService.IsWorkerProcessRunning())
        {
            ServiceRunning = true;
            _ = EnsureWorkerReadyAsync();
            return true;
        }

        if (!_workerProcessService.StartWorker())
        {
            return false;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (_workerProcessService.IsWorkerProcessRunning())
            {
                ServiceRunning = true;
                _ = EnsureWorkerReadyAsync();
                return true;
            }

            await Task.Delay(100);
        }

        await RefreshWorkerStatusAsync();
        return ServiceRunning;
    }

    public async Task StopWorkerServiceAsync()
    {
        await _workerPipeClient.SendAsync(new WorkerRequest
        {
            Command = WorkerCommandType.StopWorker
        }, 1500);

        await Task.Delay(500);
        await RefreshWorkerStatusAsync();
    }

    public void SetAutostart(bool enabled)
    {
        _autostartService.SetEnabled(enabled, _workerProcessService.WorkerExecutablePath);
        RefreshAutostartState();
    }

    public void SetTrayIconEnabled(bool enabled)
    {
        _configuration.Preferences.ShowTrayIcon = enabled;
        TrayIconEnabled = enabled;
        SaveConfiguration();
        _ = ReloadWorkerAsync();
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

    public void ImportConfiguration(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The configuration path cannot be empty.", nameof(path));
        }

        var importedConfiguration = _configService.LoadFromFile(path);
        _configService.Save(importedConfiguration);
        _configuration = importedConfiguration;
        App.ThemeService.ApplyPreference(_configuration.Theme);
        ReloadCollectionsFromConfiguration();
        OnPropertyChanged(nameof(ThemePreference));
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
            var key = keyLookup.TryGetValue(mapping.KeyId, out var keyVm) ? keyVm : null;
            mapping.UpdateDisplay(key?.ListTitle ?? "Select key");
        }

        UpdateActionSelectionState();
        OnPropertyChanged(nameof(SelectedActionOption));
    }

    public void SetSelectedActionType(string type)
    {
        ApplyActionType(type);
    }

    public void Dispose()
    {
        _statusTimer?.Stop();
    }

    private void SaveConfiguration()
    {
        _configuration.Theme = App.ThemeService.CurrentPreference;
        _configuration.Keys = KeyItems.Select(item => item.ToConfiguration()).ToList();
        _configuration.Mappings = MappingItems.Select(item => item.ToConfiguration()).ToList();
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
        TrayIconEnabled = _configuration.Preferences.ShowTrayIcon;
        SyncOsdPreferenceState();
        RefreshMappingReferences();
        SelectedKey = KeyItems.FirstOrDefault();
        SelectedMapping = MappingItems.FirstOrDefault();
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

    private void SyncBundledPresetsToConfigDirectory()
    {
        Directory.CreateDirectory(PresetDirectory);

        var bundledDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "config");
        if (!Directory.Exists(bundledDirectory))
        {
            return;
        }

        foreach (var sourcePath in Directory.GetFiles(bundledDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            var destinationPath = Path.Combine(PresetDirectory, fileName);
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
        AutostartEnabled = _autostartService.IsEnabled();
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
        RefreshAutostartState();

        var response = await _workerPipeClient.SendAsync(new WorkerRequest
        {
            Command = WorkerCommandType.GetStatus
        }, 800);

        ServiceRunning = response?.Success == true && response.Status is not null;
    }

    private async Task EnsureWorkerReadyAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await _workerPipeClient.SendAsync(new WorkerRequest
            {
                Command = WorkerCommandType.GetStatus
            }, 200);

            if (response?.Success == true && response.Status is not null)
            {
                RefreshAutostartState();
                ServiceRunning = true;
                return;
            }

            if (!_workerProcessService.IsWorkerProcessRunning())
            {
                ServiceRunning = false;
                return;
            }

            await Task.Delay(150);
        }

        await RefreshWorkerStatusAsync();
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
        ApplyActionDefaults(SelectedMapping.Action);
        RefreshMappingReferences();
    }

    private void ApplyActionDefaults(ActionDefinitionViewModel action)
    {
        if (action.Type == HotkeyActionType.ShowOsd)
        {
            if (string.IsNullOrWhiteSpace(action.OsdTitle))
            {
                action.OsdTitle = action.ActionLabel;
            }
        }
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

    private static bool IsDuplicateName(IEnumerable<(string Id, string Name)> source, string name, string? excludedId)
    {
        var normalized = NormalizeName(name, string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return source.Any(item =>
            !string.Equals(item.Id, excludedId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Name, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureUniqueName(IEnumerable<string> existingNames, string candidate, string message)
    {
        if (existingNames.Any(item => string.Equals(item, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string NormalizeName(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
