using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using MeowBox.Core.Contracts;
using MeowBox.Core.Models;
using MeowBox.Core.Services;
using MeowBox.Worker.Services;

namespace MeowBox.Worker;

internal sealed class WorkerHost : IDisposable
{
    private const int StartupShellReadyTimeoutMs = 15000;
    private const int StartupShellReadyPollMs = 250;
    private const int StartupShellReadySettleMs = 1200;

    private readonly Action _exitCallback;
    private readonly SynchronizationContext _syncContext;
    private readonly int _uiThreadId;
    private readonly AppConfigService _configService = new();
    private readonly AutostartService _autostartService = new();
    private readonly NativeActionService _nativeActionService = new();
    private readonly BatteryControlService _batteryControlService = new();
    private readonly ControllerPipeClient _controllerPipeClient = new();
    private readonly WorkerPipeServer _pipeServer;
    private readonly TouchpadInputService _touchpadInputService;
    private readonly TouchpadStreamServer _touchpadStreamServer;
    private readonly CancellationTokenSource _shellReadyCancellation = new();
    private readonly object _edgeSlideSync = new();
    private readonly object _edgeSlideHapticsSync = new();
    private readonly object _performanceModeSync = new();
    private readonly object _runtimeMappingSync = new();

    private FileSystemWatcher? _configWatcher;
    private WmiEventMonitor? _wmiMonitor;
    private TrayIconService? _trayIconService;
    private WorkerOsdService? _osdService;
    private AppConfiguration _configuration = AppConfiguration.CreateDefault();
    private IReadOnlyList<KeyDefinitionConfiguration> _runtimeKeys = [];
    private Dictionary<string, KeyActionMappingConfiguration> _runtimeMappingsByKeyId = new(StringComparer.OrdinalIgnoreCase);
    private string _lastEventSummary = "No OEM event received yet.";
    private string? _resolvedControllerPath;
    private string _stateMessage = "Starting worker";
    private volatile bool _interactiveShellReady;
    private int _pendingBrightnessEdgeSteps;
    private int _pendingVolumeEdgeSteps;
    private bool _edgeSlideProcessing;
    private bool _edgeSlideHapticsPrimed;
    private TouchpadPrivateHidService.PulseSession? _edgeSlidePulseSession;
    private int _shutdownSignaled;

    public WorkerHost(Action exitCallback)
    {
        _exitCallback = exitCallback;
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _uiThreadId = Environment.CurrentManagedThreadId;
        _interactiveShellReady = IsInteractiveShellReady();
        if (!_interactiveShellReady)
        {
            _ = MonitorInteractiveShellReadyAsync();
        }

        _pipeServer = new WorkerPipeServer(HandleRequestAsync);
        _touchpadInputService = new TouchpadInputService();
        _touchpadInputService.GestureTriggered += OnTouchpadGestureTriggered;
        _touchpadInputService.EdgeSlideTriggered += OnTouchpadEdgeSlideTriggered;
        _touchpadInputService.StateChanged += OnTouchpadStateChanged;
        _touchpadStreamServer = new TouchpadStreamServer(_touchpadInputService.GetLatestState);
        LoadConfiguration();
        StartConfigWatcher();
        _ = NotifyControllerAsync(WorkerNotificationType.Started);
        _ = Task.Run(CompleteDeferredStartupAsync);
    }

    private async Task CompleteDeferredStartupAsync()
    {
        try
        {
            StartWmiWatcher();
            EnsureAutostartRegistration();
            await RestorePreferredBatteryStateOnStartupAsync();
        }
        catch (Exception exception)
        {
            _stateMessage = exception.Message;
        }
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
        _nativeActionService.ReleaseBrightnessAdjustment();
        ReleaseEdgeSlidePulseSession();
        _configWatcher?.Dispose();
        _wmiMonitor?.Dispose();
        _pipeServer.Dispose();
        _touchpadInputService.GestureTriggered -= OnTouchpadGestureTriggered;
        _touchpadInputService.EdgeSlideTriggered -= OnTouchpadEdgeSlideTriggered;
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
            WorkerCommandType.GetBatteryControlState => Task.FromResult(new WorkerResponse
            {
                Success = true,
                Battery = _batteryControlService.QueryState(),
                Status = BuildStatus()
            }),
            WorkerCommandType.SetPerformanceMode => Task.FromResult(SetPerformanceMode(request)),
            WorkerCommandType.SetChargeLimit => Task.FromResult(SetChargeLimit(request)),
            WorkerCommandType.ReloadConfig => Task.FromResult(ReloadConfig()),
            WorkerCommandType.AnnounceState => Task.FromResult(AnnounceState()),
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

    public void OnApplicationExit()
    {
        if (Interlocked.Exchange(ref _shutdownSignaled, 1) != 0)
        {
            return;
        }

        NotifyControllerAsync(WorkerNotificationType.Stopped).GetAwaiter().GetResult();
        _autostartService.BeginDisableSilently();
    }

    private WorkerResponse SetPerformanceMode(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PerformanceModeKey))
        {
            return new WorkerResponse
            {
                Success = false,
                Error = "Performance mode was not provided.",
                Status = BuildStatus()
            };
        }

        BatteryControlState batteryState;
        ActionExecutionOsd? actionOsd;
        lock (_performanceModeSync)
        {
            batteryState = _batteryControlService.SetPerformanceModeFast(request.PerformanceModeKey);
            var title = BatteryControlCatalog.GetPerformanceModeLabel(batteryState.PerformanceModeKey);
            var assetKey = BatteryControlCatalog.GetPerformanceModeOsdAssetKey(batteryState.PerformanceModeKey);
            actionOsd = new ActionExecutionOsd(
                title,
                new IconConfiguration
                {
                    Mode = string.IsNullOrWhiteSpace(assetKey) ? IconSourceMode.None : IconSourceMode.CustomFile,
                    Path = assetKey
                });
            _stateMessage = "Performance mode set to " + title + ".";
        }

        if (actionOsd is not null)
        {
            ShowActionOsd(actionOsd);
        }

        _ = Task.Run(RefreshBatteryStateAfterMutationAsync);

        return new WorkerResponse
        {
            Success = true,
            Battery = batteryState,
            Status = BuildStatus()
        };
    }

    private WorkerResponse SetChargeLimit(WorkerRequest request)
    {
        if (request.ChargeLimitPercent is null)
        {
            return new WorkerResponse
            {
                Success = false,
                Error = "Charge limit was not provided.",
                Status = BuildStatus()
            };
        }

        var batteryState = _batteryControlService.SetChargeLimitPercentFast(request.ChargeLimitPercent.Value);
        _ = Task.Run(RefreshBatteryStateAfterMutationAsync);

        return new WorkerResponse
        {
            Success = true,
            Battery = batteryState,
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

    private WorkerResponse AnnounceState()
    {
        _ = NotifyControllerAsync(WorkerNotificationType.Started);
        return new WorkerResponse
        {
            Success = true,
            Status = BuildStatus()
        };
    }

    private void LoadConfiguration()
    {
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            Exception? capturedException = null;
            _syncContext.Send(_ =>
            {
                try
                {
                    LoadConfiguration();
                }
                catch (Exception exception)
                {
                    capturedException = exception;
                }
            }, null);

            if (capturedException is not null)
            {
                ExceptionDispatchInfo.Capture(capturedException).Throw();
            }

            return;
        }

        _configuration = _configService.Load();
        AppLanguageService.Apply(_configuration.Preferences.Language);
        RebuildRuntimeMappings();
        _nativeActionService.ReleaseBrightnessAdjustment();
        _touchpadInputService.UpdateConfiguration(_configuration.Touchpad);
        _touchpadStreamServer.Broadcast(_touchpadInputService.GetLatestState());
        ApplyTrayIconVisibility();
        _stateMessage = "Configuration loaded.";
        PrewarmEdgeSlideHapticsIfNeeded();
    }

    private void RebuildRuntimeMappings()
    {
        var runtimeKeys = _configuration.Keys
            .Select(CloneKeyDefinition)
            .Concat(SupportedDeviceConfiguration.CreateBuiltInRuntimeKeys())
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var runtimeMappings = _configuration.Mappings
            .Select(CloneMapping)
            .Concat(SupportedDeviceConfiguration.CreateBuiltInRuntimeMappings())
            .Where(item => !string.IsNullOrWhiteSpace(item.KeyId))
            .GroupBy(item => item.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        lock (_runtimeMappingSync)
        {
            _runtimeKeys = runtimeKeys;
            _runtimeMappingsByKeyId = runtimeMappings;
        }
    }

    private async Task RestorePreferredBatteryStateOnStartupAsync()
    {
        var preferredPerformanceModeKey = BatteryControlCatalog.NormalizePerformanceModeKey(
            _configuration.Preferences.PreferredPerformanceModeKey);
        var preferredChargeLimitPercent = BatteryControlCatalog.NormalizeChargeLimitPercent(
            _configuration.Preferences.PreferredChargeLimitPercent);
        var shouldRestorePerformanceMode = !_configuration.Preferences.ResetPerformanceModeToSmartOnStartup &&
                                           !string.Equals(preferredPerformanceModeKey, BatteryControlCatalog.DefaultPerformanceModeKey, StringComparison.OrdinalIgnoreCase);
        var shouldRestoreChargeLimit = !_configuration.Preferences.ResetChargeLimitToFullOnStartup &&
                                       preferredChargeLimitPercent < BatteryControlCatalog.DefaultChargeLimitPercent;
        if (!shouldRestorePerformanceMode && !shouldRestoreChargeLimit)
        {
            return;
        }

        try
        {
            await Task.Delay(2000);
            await Task.Run(() =>
            {
                if (shouldRestorePerformanceMode)
                {
                    lock (_performanceModeSync)
                    {
                        _batteryControlService.SetPerformanceModeFast(preferredPerformanceModeKey);
                    }
                }

                if (shouldRestoreChargeLimit)
                {
                    _batteryControlService.SetChargeLimitPercentFast(preferredChargeLimitPercent);
                }
            });
            await Task.Delay(900);
            await Task.Run(_batteryControlService.QueryState);
            await NotifyControllerAsync(WorkerNotificationType.Started);
        }
        catch
        {
        }
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

        IReadOnlyList<KeyDefinitionConfiguration> runtimeKeys;
        Dictionary<string, KeyActionMappingConfiguration> runtimeMappingsByKeyId;
        lock (_runtimeMappingSync)
        {
            runtimeKeys = _runtimeKeys;
            runtimeMappingsByKeyId = _runtimeMappingsByKeyId;
        }

        var matchedKey = runtimeKeys.FirstOrDefault(item => item.Trigger.IsMatch(inputEvent));
        if (matchedKey is null)
        {
            return;
        }

        runtimeMappingsByKeyId.TryGetValue(matchedKey.Id, out var mapping);

        if (mapping is not null && (mapping.Enabled || mapping.Osd.Enabled))
        {
            if (ShouldExecuteOnBackgroundThread(mapping))
            {
                _ = Task.Run(() => ExecuteMapping(mapping, inputEvent));
                return;
            }

            ExecuteMapping(mapping, inputEvent);
        }
    }

    private void ExecuteMapping(KeyActionMappingConfiguration mapping, InputEvent inputEvent)
    {
        try
        {
            ActionExecutionOsd? actionOsd = null;
            if (mapping.Enabled)
            {
                actionOsd = ExecuteAction(mapping.Action);
            }

            if (!mapping.Osd.Enabled)
            {
                return;
            }

            if (actionOsd is not null)
            {
                ShowActionOsd(actionOsd);
            }
            else if (ResolveBuiltInMappingOsd(mapping, inputEvent) is { } mappingOsd)
            {
                ShowBuiltInOsd(mappingOsd);
            }
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
            var actionOsd = ExecuteAction(action);
            if (actionOsd is not null)
            {
                ShowActionOsd(actionOsd);
            }

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

    private void OnTouchpadEdgeSlideTriggered(object? sender, TouchpadEdgeSlideEventArgs e)
    {
        if (!_configuration.Preferences.IsListening)
        {
            return;
        }

        var target = ResolveEdgeSlideTarget(e.Side);
        if (target == TouchpadEdgeSlideTarget.None)
        {
            return;
        }

        var signedSteps = e.Direction == TouchpadEdgeSlideDirection.Up ? e.Steps : -e.Steps;
        lock (_edgeSlideSync)
        {
            if (target == TouchpadEdgeSlideTarget.Brightness)
            {
                _pendingBrightnessEdgeSteps += signedSteps;
            }
            else
            {
                _pendingVolumeEdgeSteps += signedSteps;
            }

            _lastEventSummary = BuildTouchpadEdgeSlideSummary(e, target);
            if (_edgeSlideProcessing)
            {
                return;
            }

            _edgeSlideProcessing = true;
        }

        _ = Task.Run(ProcessPendingEdgeSlideAsync);
    }

    private async Task ProcessPendingEdgeSlideAsync()
    {
        while (true)
        {
            int brightnessSteps;
            int volumeSteps;
            lock (_edgeSlideSync)
            {
                brightnessSteps = _pendingBrightnessEdgeSteps;
                volumeSteps = _pendingVolumeEdgeSteps;
                _pendingBrightnessEdgeSteps = 0;
                _pendingVolumeEdgeSteps = 0;

                if (brightnessSteps == 0 && volumeSteps == 0)
                {
                    _edgeSlideProcessing = false;
                    return;
                }
            }

            try
            {
                if (brightnessSteps != 0 || volumeSteps != 0)
                {
                    EnsureEdgeSlideHapticsReady();
                }

                ApplyBrightnessEdgeSteps(brightnessSteps);
                ApplyVolumeEdgeSteps(volumeSteps);
            }
            catch (Exception exception)
            {
                _stateMessage = exception.Message;
            }
            finally
            {
                _nativeActionService.ReleaseBrightnessAdjustment();
            }

            await Task.Yield();
        }
    }

    private static bool ShouldExecuteOnBackgroundThread(KeyActionMappingConfiguration mapping)
    {
        return string.Equals(mapping.Action?.Type, HotkeyActionType.CyclePerformanceMode, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyBrightnessEdgeSteps(int steps)
    {
        if (steps == 0)
        {
            return;
        }

        for (var index = 0; index < Math.Abs(steps); index++)
        {
            if (steps > 0)
            {
                ExecuteEdgeSlideStep(_nativeActionService.BrightnessEdgeSlideUp);
            }
            else
            {
                ExecuteEdgeSlideStep(_nativeActionService.BrightnessEdgeSlideDown);
            }
        }
    }

    private void ApplyVolumeEdgeSteps(int steps)
    {
        if (steps == 0)
        {
            return;
        }

        for (var index = 0; index < Math.Abs(steps); index++)
        {
            if (steps > 0)
            {
                ExecuteEdgeSlideStep(_nativeActionService.EdgeSlideVolumeUp);
            }
            else
            {
                ExecuteEdgeSlideStep(_nativeActionService.EdgeSlideVolumeDown);
            }
        }
    }

    private void ExecuteEdgeSlideStep(Action stepAction)
    {
        if (!_edgeSlideHapticsPrimed)
        {
            EnsureEdgeSlideHapticsReady();
        }

        TryPulseEdgeSlide();
        stepAction();
    }

    private void EnsureEdgeSlideHapticsReady()
    {
        if (_edgeSlideHapticsPrimed)
        {
            return;
        }

        lock (_edgeSlideHapticsSync)
        {
            if (_edgeSlideHapticsPrimed)
            {
                return;
            }

            var touchpad = _configuration.Touchpad;
            TouchpadPrivateHidService.SetVibration(TouchpadHardwareSettings.NormalizeLevel(touchpad.FeedbackLevel));
            TouchpadPrivateHidService.SetHaptic(true);
            _edgeSlideHapticsPrimed = true;
        }
    }

    private void TryPulseEdgeSlide()
    {
        try
        {
            GetOrCreateEdgeSlidePulseSession().Pulse();
        }
        catch
        {
            ReleaseEdgeSlidePulseSession();
            _edgeSlideHapticsPrimed = false;
        }
    }

    private TouchpadPrivateHidService.PulseSession GetOrCreateEdgeSlidePulseSession()
    {
        lock (_edgeSlideHapticsSync)
        {
            _edgeSlidePulseSession ??= TouchpadPrivateHidService.CreatePulseSession();
            return _edgeSlidePulseSession;
        }
    }

    private void ReleaseEdgeSlidePulseSession()
    {
        lock (_edgeSlideHapticsSync)
        {
            _edgeSlidePulseSession?.Dispose();
            _edgeSlidePulseSession = null;
        }
    }

    private void PrewarmEdgeSlideHapticsIfNeeded()
    {
        if (ResolveEdgeSlideTarget(TouchpadEdgeSlideSide.Left) == TouchpadEdgeSlideTarget.None &&
            ResolveEdgeSlideTarget(TouchpadEdgeSlideSide.Right) == TouchpadEdgeSlideTarget.None)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                EnsureEdgeSlideHapticsReady();
            }
            catch (Exception exception)
            {
                _edgeSlideHapticsPrimed = false;
                _stateMessage = exception.Message;
            }
        });
    }

    private ActionExecutionOsd? ExecuteAction(ActionDefinitionConfiguration action)
    {
        switch (action.Type)
        {
            case HotkeyActionType.None:
                return null;
            case HotkeyActionType.CyclePerformanceMode:
                return ExecuteCyclePerformanceModeAction();
            case HotkeyActionType.SendStandardKey:
                _nativeActionService.SendConfiguredKeyChord(action.KeyChord);
                return null;
            case HotkeyActionType.OpenSettings:
                _nativeActionService.OpenSettings();
                return null;
            case HotkeyActionType.OpenProjection:
                _nativeActionService.OpenProjection();
                return null;
            case HotkeyActionType.ToggleTouchpad:
                _nativeActionService.ToggleTouchpad();
                return null;
            case HotkeyActionType.MicrophoneMuteOn:
                AudioEndpointController.SetCaptureMute(true);
                return null;
            case HotkeyActionType.MicrophoneMuteOff:
                AudioEndpointController.SetCaptureMute(false);
                return null;
            case HotkeyActionType.VolumeUp:
                _nativeActionService.VolumeUp();
                return null;
            case HotkeyActionType.VolumeDown:
                _nativeActionService.VolumeDown();
                return null;
            case HotkeyActionType.VolumeMute:
                _nativeActionService.VolumeMute();
                return null;
            case HotkeyActionType.MediaPrevious:
                _nativeActionService.MediaPrevious();
                return null;
            case HotkeyActionType.MediaNext:
                _nativeActionService.MediaNext();
                return null;
            case HotkeyActionType.MediaPlayPause:
                _nativeActionService.MediaPlayPause();
                return null;
            case HotkeyActionType.BrightnessUp:
                _nativeActionService.BrightnessUp();
                return null;
            case HotkeyActionType.BrightnessDown:
                _nativeActionService.BrightnessDown();
                return null;
            case HotkeyActionType.ToggleAirplaneMode:
                _ = _nativeActionService.ToggleAirplaneModeAsync();
                return null;
            case HotkeyActionType.LockWindows:
                _nativeActionService.LockWindows();
                return null;
            case HotkeyActionType.Screenshot:
                _nativeActionService.Screenshot();
                return null;
            case HotkeyActionType.OpenCalculator:
                _nativeActionService.OpenCalculator();
                return null;
            case HotkeyActionType.OpenApplication:
                _nativeActionService.LaunchConfiguredTarget(action.Target ?? string.Empty, action.Arguments);
                return null;
            default:
                return null;
        }
    }

    private ActionExecutionOsd ExecuteCyclePerformanceModeAction()
    {
        lock (_performanceModeSync)
        {
            var state = _batteryControlService.TryGetCachedState(out var cachedState)
                ? cachedState
                : _batteryControlService.QueryState();
            if (!state.Supported)
            {
                throw new InvalidOperationException(LocalizedText.Pick("The device does not expose the performance mode controls.", "设备没有暴露性能模式控制接口。"));
            }

            var nextModeKey = BatteryControlCatalog.GetNextCyclePerformanceModeKey(state.PerformanceModeKey);
            var updatedState = _batteryControlService.SetPerformanceModeFast(nextModeKey);
            _ = Task.Run(RefreshBatteryStateAfterMutationAsync);
            var title = BatteryControlCatalog.GetPerformanceModeLabel(updatedState.PerformanceModeKey);
            var assetKey = BatteryControlCatalog.GetPerformanceModeOsdAssetKey(updatedState.PerformanceModeKey);
            _stateMessage = "Performance mode set to " + title + ".";

            return new ActionExecutionOsd(
                title,
                new IconConfiguration
                {
                    Mode = string.IsNullOrWhiteSpace(assetKey) ? IconSourceMode.None : IconSourceMode.CustomFile,
                    Path = assetKey
                });
        }
    }

    private async Task RefreshBatteryStateAfterMutationAsync()
    {
        try
        {
            await Task.Delay(700);
            _batteryControlService.QueryState();
            await NotifyControllerAsync(WorkerNotificationType.Started);
        }
        catch
        {
        }
    }

    private static BuiltInOsdDefinition? ResolveBuiltInMappingOsd(KeyActionMappingConfiguration mapping, InputEvent inputEvent)
    {
        return BuiltInOsdCatalog.ResolveForKey(mapping.KeyId, inputEvent.ReportHex);
    }

    private void ShowActionOsd(ActionExecutionOsd actionOsd)
    {
        ShowOsd(actionOsd.Title, actionOsd.Icon);
    }

    private void ShowBuiltInOsd(BuiltInOsdDefinition osd)
    {
        ShowOsd(
            osd.Title,
            new IconConfiguration
            {
                Mode = string.IsNullOrWhiteSpace(osd.AssetKey) ? IconSourceMode.None : IconSourceMode.CustomFile,
                Path = osd.AssetKey
            });
    }

    private void ShowOsd(string title, IconConfiguration icon)
    {
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
            _ => "main region"
        };

        return e.TriggerKind switch
            {
                TouchpadGestureTriggerKind.LongPress => $"Touchpad long press ({regionLabel})",
                TouchpadGestureTriggerKind.DeepPress when !string.IsNullOrWhiteSpace(e.RegionId) => $"Touchpad deep press ({regionLabel})",
            _ => "Touchpad deep press (main region)"
            };
    }

    private TouchpadEdgeSlideTarget ResolveEdgeSlideTarget(TouchpadEdgeSlideSide side)
    {
        var action = side == TouchpadEdgeSlideSide.Left
            ? _configuration.Touchpad.LeftEdgeSlideAction
            : _configuration.Touchpad.RightEdgeSlideAction;

        return action?.Type switch
        {
            HotkeyActionType.BrightnessUp or HotkeyActionType.BrightnessDown => TouchpadEdgeSlideTarget.Brightness,
            HotkeyActionType.VolumeUp or HotkeyActionType.VolumeDown => TouchpadEdgeSlideTarget.Volume,
            _ => TouchpadEdgeSlideTarget.None
        };
    }

    private static string BuildTouchpadEdgeSlideSummary(TouchpadEdgeSlideEventArgs e, TouchpadEdgeSlideTarget target)
    {
        var sideLabel = e.Side == TouchpadEdgeSlideSide.Left ? "left edge" : "right edge";
        var targetLabel = target == TouchpadEdgeSlideTarget.Brightness ? "brightness" : "volume";
        var directionLabel = e.Direction == TouchpadEdgeSlideDirection.Up ? "up" : "down";
        return $"Touchpad {sideLabel} slide {directionLabel} ({targetLabel}) x{e.Steps}";
    }

    private static KeyDefinitionConfiguration CloneKeyDefinition(KeyDefinitionConfiguration key)
    {
        return new KeyDefinitionConfiguration
        {
            Id = key.Id,
            Name = key.Name,
            Trigger = key.Trigger ?? new EventMatcherConfiguration()
        };
    }

    private enum TouchpadEdgeSlideTarget
    {
        None,
        Brightness,
        Volume
    }

    private static KeyActionMappingConfiguration CloneMapping(KeyActionMappingConfiguration mapping)
    {
        return new KeyActionMappingConfiguration
        {
            Id = mapping.Id,
            Name = mapping.Name,
            Enabled = mapping.Enabled,
            KeyId = mapping.KeyId,
            Action = new ActionDefinitionConfiguration
            {
                Type = mapping.Action?.Type ?? HotkeyActionType.None,
                KeyChord = mapping.Action?.KeyChord is null
                    ? null
                    : new KeyChordConfiguration
                    {
                        PrimaryKey = mapping.Action.KeyChord.PrimaryKey,
                        Modifiers = [.. mapping.Action.KeyChord.Modifiers]
                    },
                Target = mapping.Action?.Target,
                Arguments = mapping.Action?.Arguments
            },
            Osd = new MappingOsdConfiguration
            {
                Enabled = mapping.Osd?.Enabled == true
            }
        };
    }

    private WorkerStatus BuildStatus()
    {
        var batteryState = _batteryControlService.TryGetCachedState(out var cachedBatteryState)
            ? cachedBatteryState
            : null;

        return new WorkerStatus
        {
            IsRunning = true,
            IsElevated = IsCurrentProcessElevated(),
            IsListening = _configuration.Preferences.IsListening,
            IsTrayIconVisible = _configuration.Preferences.ShowTrayIcon,
            LastEventSummary = _lastEventSummary,
            ConfigPath = _configService.ConfigPath,
            StateMessage = _stateMessage,
            Battery = batteryState,
            Touchpad = _touchpadInputService.GetLatestState()
        };
    }

    private void EnsureAutostartRegistration()
    {
        try
        {
            if (!IsCurrentProcessElevated())
            {
                return;
            }

            var workerExecutablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(workerExecutablePath) || !File.Exists(workerExecutablePath))
            {
                _stateMessage = "Worker executable path not found.";
                return;
            }

            if (_autostartService.HasMatchingPriorityStartupTask(workerExecutablePath))
            {
                return;
            }

            _autostartService.SetEnabled(true, workerExecutablePath, preferPriorityStartup: true);
        }
        catch (Exception exception)
        {
            _stateMessage = "Failed to register startup task: " + exception.Message;
        }
    }

    private async Task NotifyControllerAsync(string notificationType)
    {
        await _controllerPipeClient.SendAsync(new WorkerNotification
        {
            Type = notificationType,
            Status = BuildStatus()
        });
    }

    private static bool IsCurrentProcessElevated()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
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
        _trayIconService ??= new TrayIconService(OpenController, RequestHardExit);
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

    private sealed record ActionExecutionOsd(string Title, IconConfiguration Icon);

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

    private void RequestHardExit()
    {
        Environment.Exit(0);
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateDirectControllerCandidates())
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        if (repositoryRoot is null)
        {
            yield break;
        }

        var controllerBuildRoot = Path.Combine(repositoryRoot, "build", "bin", "MeowBox.Controller");
        if (Directory.Exists(controllerBuildRoot))
        {
            IEnumerable<string> discoveredCandidates;
            try
            {
                discoveredCandidates = Directory
                    .EnumerateFiles(controllerBuildRoot, "MeowBox.Controller.exe", SearchOption.AllDirectories)
                    .OrderByDescending(static path => File.GetLastWriteTimeUtc(path));
            }
            catch
            {
                discoveredCandidates = [];
            }

            foreach (var candidate in discoveredCandidates)
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectControllerCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "MeowBox.Controller.exe");
        yield return Path.Combine(baseDirectory, "..", "MeowBox.Controller.exe");
        yield return Path.Combine(baseDirectory, "..", "..", "MeowBox.Controller.exe");

        var current = new DirectoryInfo(baseDirectory);
        for (var depth = 0; depth < 6 && current is not null; depth++, current = current.Parent)
        {
            yield return Path.Combine(current.FullName, "build", "bin", "MeowBox.Controller", "net8.0-windows10.0.19041.0", "MeowBox.Controller.exe");
            yield return Path.Combine(current.FullName, "build", "bin", "MeowBox.Controller", "net8.0-windows10.0.19041.0", "win-x64", "MeowBox.Controller.exe");
            yield return Path.Combine(current.FullName, "artifacts", "MeowBox", "MeowBox.Controller.exe");
            yield return Path.Combine(current.FullName, "build", "publish", "controller", "MeowBox.Controller.exe");
            yield return Path.Combine(current.FullName, "build", "package", "MeowBox", "MeowBox.Controller.exe");
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
            var controllerProjectDirectory = Path.Combine(directory.FullName, "src", "MeowBox.Controller");
            if (Directory.Exists(controllerProjectDirectory))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
