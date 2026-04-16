using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FnMappingTool.Core.Contracts;
using FnMappingTool.Core.Models;
using FnMappingTool.Core.Services;
using FnMappingTool.Worker.Services;

namespace FnMappingTool.Worker;

internal sealed class WorkerHost : IDisposable
{
    private const int StartupShellReadyTimeoutMs = 15000;
    private const int StartupShellReadyPollMs = 250;
    private const int StartupShellReadySettleMs = 1200;

    private readonly Action _exitCallback;
    private readonly SynchronizationContext _syncContext;
    private readonly AppConfigService _configService = new();
    private readonly NativeActionService _nativeActionService = new();
    private readonly WorkerPipeServer _pipeServer;
    private readonly TouchpadInputService _touchpadInputService;
    private readonly TouchpadStreamServer _touchpadStreamServer;
    private readonly CancellationTokenSource _shellReadyCancellation = new();

    private FileSystemWatcher? _configWatcher;
    private WmiEventMonitor? _wmiMonitor;
    private TrayIconService? _trayIconService;
    private WorkerOsdService? _osdService;
    private AppConfiguration _configuration = AppConfiguration.CreateDefault();
    private string _lastEventSummary = "No OEM event received yet.";
    private string? _resolvedControllerPath;
    private string _stateMessage = "Starting worker";
    private volatile bool _interactiveShellReady;

    public WorkerHost(Action exitCallback)
    {
        _exitCallback = exitCallback;
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _interactiveShellReady = IsInteractiveShellReady();
        if (!_interactiveShellReady)
        {
            _ = MonitorInteractiveShellReadyAsync();
        }

        _pipeServer = new WorkerPipeServer(HandleRequestAsync);
        _touchpadInputService = new TouchpadInputService();
        _touchpadInputService.GestureTriggered += OnTouchpadGestureTriggered;
        _touchpadInputService.StateChanged += OnTouchpadStateChanged;
        _touchpadStreamServer = new TouchpadStreamServer(_touchpadInputService.GetLatestState);
        LoadConfiguration();
        StartConfigWatcher();
        StartWmiWatcher();
    }

    private async Task MonitorInteractiveShellReadyAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                if (!WaitForInteractiveShellReady(StartupShellReadyTimeoutMs, _shellReadyCancellation.Token))
                {
                    return;
                }

                MarkInteractiveShellReady();
            }, _shellReadyCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsInteractiveShellReady()
    {
        return GetShellWindow() != IntPtr.Zero &&
               FindWindow("Shell_TrayWnd", null) != IntPtr.Zero &&
               GetForegroundWindow() != IntPtr.Zero;
    }


    private static bool WaitForInteractiveShellReady(int timeoutMs, CancellationToken cancellationToken)
    {
        if (IsInteractiveShellReady())
        {
            return true;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(StartupShellReadyPollMs);
            if (!IsInteractiveShellReady())
            {
                continue;
            }

            Thread.Sleep(StartupShellReadySettleMs);
            return true;
        }

        return false;
    }

    private void MarkInteractiveShellReady()
    {
        if (_interactiveShellReady)
        {
            return;
        }

        _interactiveShellReady = true;
        _syncContext.Post(_ => ApplyTrayIconVisibility(), null);
    }

    private bool EnsureInteractiveShellReadyForUi(int timeoutMs)
    {
        if (_interactiveShellReady)
        {
            return true;
        }

        if (!WaitForInteractiveShellReady(timeoutMs, _shellReadyCancellation.Token))
        {
            return false;
        }

        MarkInteractiveShellReady();
        return true;
    }

    public void Dispose()
    {
        _shellReadyCancellation.Cancel();
        _configWatcher?.Dispose();
        _wmiMonitor?.Dispose();
        _pipeServer.Dispose();
        _touchpadInputService.GestureTriggered -= OnTouchpadGestureTriggered;
        _touchpadInputService.StateChanged -= OnTouchpadStateChanged;
        _touchpadStreamServer.Dispose();
        _touchpadInputService.Dispose();
        _trayIconService?.Dispose();
        _osdService?.Dispose();
        _shellReadyCancellation.Dispose();
    }

    private Task<WorkerResponse> HandleRequestAsync(WorkerRequest request)
    {
        return request.Command switch
        {
            WorkerCommandType.GetStatus => Task.FromResult(new WorkerResponse
            {
                Success = true,
                Status = BuildStatus()
            }),
            WorkerCommandType.ReloadConfig => Task.FromResult(ReloadConfig()),
            WorkerCommandType.StopWorker => Task.FromResult(StopWorker()),
            _ => Task.FromResult(new WorkerResponse
            {
                Success = false,
                Error = "Unknown worker command."
            })
        };
    }

    private WorkerResponse ReloadConfig()
    {
        LoadConfiguration();
        return new WorkerResponse
        {
            Success = true,
            Status = BuildStatus()
        };
    }

    private WorkerResponse StopWorker()
    {
        _syncContext.Post(_ => _exitCallback(), null);
        return new WorkerResponse
        {
            Success = true
        };
    }

    private void LoadConfiguration()
    {
        _configuration = _configService.Load();
        _touchpadInputService.UpdateConfiguration(_configuration.Touchpad);
        _touchpadStreamServer.Broadcast(_touchpadInputService.GetLatestState());
        ApplyTrayIconVisibility();
        _stateMessage = "Configuration loaded.";
    }

    private void StartConfigWatcher()
    {
        Directory.CreateDirectory(_configService.ConfigDirectory);
        _configWatcher = new FileSystemWatcher(_configService.ConfigDirectory, Path.GetFileName(_configService.ConfigPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _configWatcher.Changed += (_, _) => ReloadFromDiskDebounced();
        _configWatcher.Created += (_, _) => ReloadFromDiskDebounced();
        _configWatcher.Renamed += (_, _) => ReloadFromDiskDebounced();
        _configWatcher.EnableRaisingEvents = true;
    }

    private async void ReloadFromDiskDebounced()
    {
        await Task.Delay(150);
        try
        {
            LoadConfiguration();
        }
        catch
        {
        }
    }

    private void StartWmiWatcher()
    {
        _wmiMonitor?.Dispose();
        _wmiMonitor = new WmiEventMonitor(OnInputEvent, status => _stateMessage = status);
        var started = _wmiMonitor.Start();
        _stateMessage = started.Count > 0
            ? "Watching " + string.Join(", ", started)
            : "No OEM WMI class could be subscribed.";
    }

    private void OnInputEvent(InputEvent inputEvent)
    {
        _lastEventSummary = BuildEventSummary(inputEvent);

        if (!_configuration.Preferences.IsListening)
        {
            return;
        }

        var matchedKey = _configuration.Keys.FirstOrDefault(item => item.Trigger.IsMatch(inputEvent));
        if (matchedKey is null)
        {
            return;
        }

        var mapping = _configuration.Mappings.FirstOrDefault(item =>
            string.Equals(item.KeyId, matchedKey.Id, StringComparison.OrdinalIgnoreCase));

        if (mapping is not null && (mapping.Enabled || mapping.Osd.Enabled))
        {
            ExecuteMapping(mapping);
        }
    }

    private void ExecuteMapping(KeyActionMappingConfiguration mapping)
    {
        try
        {
            if (mapping.Enabled)
            {
                ExecuteAction(mapping.Action);
            }

            ShowMappingOsd(mapping);
        }
        catch (Exception exception)
        {
            _stateMessage = exception.Message;
        }
    }

    private void OnTouchpadGestureTriggered(object? sender, TouchpadGestureTriggerEventArgs e)
    {
        if (!_configuration.Preferences.IsListening || !_configuration.Touchpad.Enabled)
        {
            return;
        }

        var action = ResolveTouchpadAction(e);
        if (action is null || string.IsNullOrWhiteSpace(action.Type))
        {
            return;
        }

        try
        {
            ExecuteAction(action);
            _lastEventSummary = BuildTouchpadGestureSummary(e);
        }
        catch (Exception exception)
        {
            _stateMessage = exception.Message;
        }
    }

    private void OnTouchpadStateChanged(object? sender, TouchpadLiveStateSnapshot state)
    {
        _touchpadStreamServer.Broadcast(state);
    }

    private void ExecuteAction(ActionDefinitionConfiguration action)
    {
        switch (action.Type)
        {
            case HotkeyActionType.None:
            case HotkeyActionType.ShowOsd:
                return;
            case HotkeyActionType.SendStandardKey:
                _nativeActionService.SendConfiguredStandardKey(action.StandardKey);
                break;
            case HotkeyActionType.OpenSettings:
                _nativeActionService.OpenSettings();
                break;
            case HotkeyActionType.OpenProjection:
                _nativeActionService.OpenProjection();
                break;
            case HotkeyActionType.MicrophoneMuteOn:
                AudioEndpointController.SetCaptureMute(true);
                break;
            case HotkeyActionType.MicrophoneMuteOff:
                AudioEndpointController.SetCaptureMute(false);
                break;
            case HotkeyActionType.VolumeUp:
                _nativeActionService.VolumeUp();
                break;
            case HotkeyActionType.VolumeDown:
                _nativeActionService.VolumeDown();
                break;
            case HotkeyActionType.VolumeMute:
                _nativeActionService.VolumeMute();
                break;
            case HotkeyActionType.MediaPrevious:
                _nativeActionService.MediaPrevious();
                break;
            case HotkeyActionType.MediaNext:
                _nativeActionService.MediaNext();
                break;
            case HotkeyActionType.MediaPlayPause:
                _nativeActionService.MediaPlayPause();
                break;
            case HotkeyActionType.BrightnessUp:
                _nativeActionService.BrightnessUp();
                break;
            case HotkeyActionType.BrightnessDown:
                _nativeActionService.BrightnessDown();
                break;
            case HotkeyActionType.ToggleAirplaneMode:
                _ = _nativeActionService.ToggleAirplaneModeAsync();
                break;
            case HotkeyActionType.LockWindows:
                _nativeActionService.LockWindows();
                break;
            case HotkeyActionType.Screenshot:
                _nativeActionService.Screenshot();
                break;
            case HotkeyActionType.OpenCalculator:
                _nativeActionService.OpenCalculator();
                break;
            case HotkeyActionType.OpenApplication:
                _nativeActionService.LaunchConfiguredTarget(action.Target ?? string.Empty, action.Arguments);
                break;
            default:
                return;
        }
    }

    private void ShowMappingOsd(KeyActionMappingConfiguration mapping)
    {
        if (!mapping.Osd.Enabled)
        {
            return;
        }

        var title = ResolveMappingOsdTitle(mapping);
        var icon = mapping.Osd.Icon ?? new IconConfiguration();

        _syncContext.Post(_ =>
        {
            try
            {
                if (!EnsureInteractiveShellReadyForUi(4000))
                {
                    _stateMessage = "Windows shell is still starting. Try again in a moment.";
                    return;
                }

                GetOsdService().Show(
                    title,
                    icon,
                    _configuration.Preferences.Osd,
                    _configuration.Theme);
            }
            catch (Exception exception)
            {
                _stateMessage = exception.Message;
            }
        }, null);
    }

    private static string ResolveMappingOsdTitle(KeyActionMappingConfiguration mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.Osd.Title))
        {
            return mapping.Osd.Title;
        }

        return !string.IsNullOrWhiteSpace(mapping.Action.Type)
            ? ActionCatalog.GetLabel(mapping.Action.Type)
            : MappingDisplayCatalog.ShowOsdLabel;
    }

    private ActionDefinitionConfiguration? ResolveTouchpadAction(TouchpadGestureTriggerEventArgs e)
    {
        var touchpad = _configuration.Touchpad ?? new TouchpadConfiguration();
        return e.TriggerKind switch
        {
            TouchpadGestureTriggerKind.LongPress => ResolveCornerRegion(e.RegionId)?.LongPressAction,
            TouchpadGestureTriggerKind.DeepPress => ResolveCornerRegion(e.RegionId)?.DeepPressAction is { Type.Length: > 0 } cornerDeepPress
                ? cornerDeepPress
                : touchpad.DeepPressAction,
            _ => null
        };
    }

    private TouchpadCornerRegionConfiguration? ResolveCornerRegion(string? regionId)
    {
        return regionId switch
        {
            TouchpadCornerRegionId.LeftTop => _configuration.Touchpad.LeftTopCorner,
            TouchpadCornerRegionId.RightTop => _configuration.Touchpad.RightTopCorner,
            _ => null
        };
    }

    private static string BuildTouchpadGestureSummary(TouchpadGestureTriggerEventArgs e)
    {
        var regionLabel = e.RegionId switch
        {
            TouchpadCornerRegionId.LeftTop => "left-top corner",
            TouchpadCornerRegionId.RightTop => "right-top corner",
            _ => "surface"
        };

        return e.TriggerKind switch
        {
            TouchpadGestureTriggerKind.LongPress => $"Touchpad long press ({regionLabel})",
            TouchpadGestureTriggerKind.DeepPress when !string.IsNullOrWhiteSpace(e.RegionId) => $"Touchpad deep press ({regionLabel})",
            _ => "Touchpad deep press"
        };
    }

    private WorkerStatus BuildStatus()
    {
        return new WorkerStatus
        {
            IsRunning = true,
            IsListening = _configuration.Preferences.IsListening,
            IsTrayIconVisible = _configuration.Preferences.ShowTrayIcon,
            LastEventSummary = _lastEventSummary,
            ConfigPath = _configService.ConfigPath,
            StateMessage = _stateMessage,
            Touchpad = _touchpadInputService.GetLatestState()
        };
    }

    private static string BuildEventSummary(InputEvent inputEvent)
    {
        if (string.Equals(inputEvent.Source, InputSourceKind.Wmi, StringComparison.OrdinalIgnoreCase))
        {
            return $"{inputEvent.WmiClassName}  {inputEvent.ReportHex}";
        }

        return $"{inputEvent.Source}  VK={inputEvent.VirtualKey}  Scan={inputEvent.MakeCode}";
    }

    private TrayIconService GetTrayIconService()
    {
        _trayIconService ??= new TrayIconService(OpenController, RequestExit);
        return _trayIconService;
    }

    private void ApplyTrayIconVisibility()
    {
        if (!_interactiveShellReady)
        {
            _trayIconService?.SetVisible(false);
            return;
        }

        if (_configuration.Preferences.ShowTrayIcon)
        {
            GetTrayIconService().SetVisible(true);
            return;
        }

        _trayIconService?.SetVisible(false);
    }

    private WorkerOsdService GetOsdService()
    {
        _osdService ??= new WorkerOsdService();
        return _osdService;
    }

    private void OpenController()
    {
        try
        {
            var controllerPath = ResolveControllerPath();
            if (string.IsNullOrWhiteSpace(controllerPath))
            {
                _stateMessage = "Controller executable not found.";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = controllerPath,
                WorkingDirectory = Path.GetDirectoryName(controllerPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _stateMessage = "Failed to open Controller: " + exception.Message;
        }
    }

    private void RequestExit()
    {
        _syncContext.Post(_ => _exitCallback(), null);
    }

    private string? ResolveControllerPath()
    {
        if (!string.IsNullOrWhiteSpace(_resolvedControllerPath) && File.Exists(_resolvedControllerPath))
        {
            return _resolvedControllerPath;
        }

        foreach (var candidate in EnumerateControllerCandidates())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            _resolvedControllerPath = candidate;
            return candidate;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateControllerCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "FnMappingTool.Controller.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "..", "FnMappingTool.Controller.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "FnMappingTool.Controller.exe");

        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        if (repositoryRoot is null)
        {
            yield break;
        }

        var controllerBuildRoot = Path.Combine(repositoryRoot, "build", "bin", "FnMappingTool.Controller");
        if (!Directory.Exists(controllerBuildRoot))
        {
            yield break;
        }

        IEnumerable<string> discoveredCandidates;
        try
        {
            discoveredCandidates = Directory
                .EnumerateFiles(controllerBuildRoot, "FnMappingTool.Controller.exe", SearchOption.AllDirectories)
                .OrderByDescending(static path => File.GetLastWriteTimeUtc(path));
        }
        catch
        {
            yield break;
        }

        foreach (var candidate in discoveredCandidates)
        {
            yield return candidate;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static string? FindRepositoryRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            var controllerProjectDirectory = Path.Combine(directory.FullName, "src", "FnMappingTool.Controller");
            if (Directory.Exists(controllerProjectDirectory))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
