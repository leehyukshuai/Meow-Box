using System.Diagnostics;
using System.Threading.Tasks;
using FnMappingTool.Core.Contracts;
using FnMappingTool.Core.Models;
using FnMappingTool.Core.Services;
using FnMappingTool.Worker.Services;

namespace FnMappingTool.Worker;

internal sealed class WorkerHost : IDisposable
{
    private readonly Action _exitCallback;
    private readonly SynchronizationContext _syncContext;
    private readonly AppConfigService _configService = new();
    private readonly NativeActionService _nativeActionService = new();
    private readonly WorkerPipeServer _pipeServer;

    private FileSystemWatcher? _configWatcher;
    private WmiEventMonitor? _wmiMonitor;
    private TrayIconService? _trayIconService;
    private WorkerOsdService? _osdService;
    private AppConfiguration _configuration = AppConfiguration.CreateDefault();
    private TaskCompletionSource<InputEvent?>? _captureRequest;
    private string _lastEventSummary = "No OEM event captured yet.";
    private string? _resolvedControllerPath;
    private string _stateMessage = "Starting worker";

    public WorkerHost(Action exitCallback)
    {
        _exitCallback = exitCallback;
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _pipeServer = new WorkerPipeServer(HandleRequestAsync);
        LoadConfiguration();
        StartConfigWatcher();
        StartWmiWatcher();
    }

    public void Dispose()
    {
        _configWatcher?.Dispose();
        _wmiMonitor?.Dispose();
        _pipeServer.Dispose();
        _trayIconService?.Dispose();
        _osdService?.Dispose();
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
            WorkerCommandType.CaptureNextEvent => CaptureNextEventAsync(),
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

    private async Task<WorkerResponse> CaptureNextEventAsync()
    {
        var request = new TaskCompletionSource<InputEvent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _captureRequest = request;
        _stateMessage = "Waiting for next OEM event for capture.";

        var result = await request.Task;
        return new WorkerResponse
        {
            Success = result is not null,
            CapturedEvent = result,
            Status = BuildStatus(),
            Error = result is null ? "No event captured." : null
        };
    }

    private void LoadConfiguration()
    {
        _configuration = _configService.Load();
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

        var captureRequest = _captureRequest;
        if (captureRequest is not null)
        {
            _captureRequest = null;
            captureRequest.TrySetResult(inputEvent);
        }

        if (!_configuration.Preferences.IsListening)
        {
            return;
        }

        var matchingKeyIds = _configuration.Keys
            .Where(item => item.Trigger.IsMatch(inputEvent))
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (matchingKeyIds.Count == 0)
        {
            return;
        }

        foreach (var mapping in _configuration.Mappings.Where(item => item.Enabled && matchingKeyIds.Contains(item.KeyId)))
        {
            ExecuteMapping(mapping);
        }
    }

    private void ExecuteMapping(KeyActionMappingConfiguration mapping)
    {
        var action = mapping.Action;

        try
        {
            switch (action.Type)
            {
                case HotkeyActionType.None:
                    return;
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
                case HotkeyActionType.ShowOsd:
                    _syncContext.Post(_ =>
                    {
                        try
                        {
                            GetOsdService().Show(
                                action.OsdTitle ?? ActionCatalog.GetLabel(action.Type),
                                action.OsdMessage,
                                action.OsdIcon,
                                action.DurationMs ?? RuntimeDefaults.DefaultOsdDurationMs);
                        }
                        catch (Exception exception)
                        {
                            _stateMessage = exception.Message;
                        }
                    }, null);
                    break;
                case HotkeyActionType.OpenApplication:
                    _nativeActionService.LaunchConfiguredTarget(action.Target ?? string.Empty, action.Arguments);
                    break;
                default:
                    return;
            }
        }
        catch (Exception exception)
        {
            _stateMessage = exception.Message;
        }
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
            StateMessage = _stateMessage
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
