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
    private const int BatteryAutomationPollIntervalMs = 2000;
    private readonly Action _exitCallback;
    private readonly SynchronizationContext _syncContext;
    private readonly int _uiThreadId;
    private readonly AppConfigService _configService = new();
    private readonly AutostartService _autostartService = new();
    private readonly NativeActionService _nativeActionService = new();
    private readonly BatteryControlService _batteryControlService = new();
    private readonly WindowsPowerModeService _windowsPowerModeService = new();
    private readonly ControllerPipeClient _controllerPipeClient = new();
    private readonly WorkerPipeServer _pipeServer;
    private readonly TouchpadInputService _touchpadInputService;
    private readonly TouchpadStreamServer _touchpadStreamServer;
    private readonly TouchpadEdgeSlideService _touchpadEdgeSlideService;
    private readonly CancellationTokenSource _shellReadyCancellation = new();
    private readonly object _performanceModeSync = new();
    private readonly object _runtimeMappingSync = new();

    private System.Threading.Timer? _batteryAutomationTimer;
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
    private int _batteryAutomationTickInProgress;
    private int _lastSyncedBatteryModeOnDcThresholdPercent = int.MinValue;
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
        _touchpadEdgeSlideService = new TouchpadEdgeSlideService(
            _nativeActionService,
            () => _configuration.Touchpad,
            summary => _lastEventSummary = summary,
            message => _stateMessage = message);
        _touchpadInputService.GestureTriggered += OnTouchpadGestureTriggered;
        _touchpadInputService.EdgeSlideTriggered += OnTouchpadEdgeSlideTriggered;
        _touchpadInputService.StateChanged += OnTouchpadStateChanged;
        _touchpadStreamServer = new TouchpadStreamServer(_touchpadInputService.GetLatestState);
        LoadConfiguration();
        StartConfigWatcher();
        StartBatteryAutomationMonitor();
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
        _touchpadEdgeSlideService.Dispose();
        _configWatcher?.Dispose();
        _wmiMonitor?.Dispose();
        _pipeServer.Dispose();
        _touchpadInputService.GestureTriggered -= OnTouchpadGestureTriggered;
        _touchpadInputService.EdgeSlideTriggered -= OnTouchpadEdgeSlideTriggered;
        _touchpadInputService.StateChanged -= OnTouchpadStateChanged;
        _touchpadStreamServer.Dispose();
        _touchpadInputService.Dispose();
        _batteryAutomationTimer?.Dispose();
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
                Battery = DecorateBatteryState(_batteryControlService.QueryState()),
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
        (batteryState, actionOsd) = ApplyPerformanceMode(request.PerformanceModeKey);

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

        var batteryState = DecorateBatteryState(_batteryControlService.SetChargeLimitPercentFast(request.ChargeLimitPercent.Value));
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
        SyncWindowsBatterySaverThresholdPreference();
        RebuildRuntimeMappings();
        _nativeActionService.ReleaseBrightnessAdjustment();
        _touchpadInputService.UpdateConfiguration(_configuration.Touchpad);
        _touchpadStreamServer.Broadcast(_touchpadInputService.GetLatestState());
        ApplyTrayIconVisibility();
        _stateMessage = "Configuration loaded.";
        _touchpadEdgeSlideService.PrewarmIfNeeded(
            ResolveEdgeSlideTarget(TouchpadEdgeSlideSide.Left) != TouchpadEdgeSlideService.TouchpadEdgeSlideTarget.None ||
            ResolveEdgeSlideTarget(TouchpadEdgeSlideSide.Right) != TouchpadEdgeSlideService.TouchpadEdgeSlideTarget.None);
    }

    private void SyncWindowsBatterySaverThresholdPreference()
    {
        var batteryModeOnDcThresholdPercent = BatteryControlCatalog.NormalizeBatteryModeOnDcThresholdPercent(
            _configuration.Preferences.SwitchToBatteryModeOnDcThresholdPercent);
        if (batteryModeOnDcThresholdPercent == _lastSyncedBatteryModeOnDcThresholdPercent)
        {
            return;
        }

        try
        {
            _windowsPowerModeService.SetEnergySaverBatteryThresholdPercent(batteryModeOnDcThresholdPercent);
            _lastSyncedBatteryModeOnDcThresholdPercent = batteryModeOnDcThresholdPercent;
        }
        catch (Exception exception)
        {
            _stateMessage = "Could not sync the Windows Energy Saver threshold: " + exception.Message;
        }
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
        var startupCycleModeKey = BatteryControlCatalog.GetDefaultPerformanceModeCycleKey(
            _configuration.Preferences.PerformanceModeCycleKeys);
        var preferredChargeLimitPercent = BatteryControlCatalog.NormalizeChargeLimitPercent(
            _configuration.Preferences.PreferredChargeLimitPercent);
        var shouldRestorePerformanceMode = true;
        var shouldRestoreChargeLimit = !_configuration.Preferences.ResetChargeLimitToFullOnStartup &&
                                       preferredChargeLimitPercent < BatteryControlCatalog.DefaultChargeLimitPercent;
        if (!shouldRestorePerformanceMode && !shouldRestoreChargeLimit)
        {
            return;
        }

        try
        {
            await Task.Delay(2000);
            var skippedBecauseBatterySaverWasAlreadyActive = false;
            await Task.Run(() =>
            {
                var currentState = DecorateBatteryState(_batteryControlService.QueryState());
                var batterySaverAlreadyActive =
                    string.Equals(
                        BatteryControlCatalog.NormalizePerformanceModeKey(currentState.PerformanceModeKey),
                        BatteryControlCatalog.Battery,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(currentState.SelectedPerformanceModeKey),
                        BatteryControlCatalog.Battery,
                        StringComparison.OrdinalIgnoreCase);
                if (batterySaverAlreadyActive)
                {
                    skippedBecauseBatterySaverWasAlreadyActive = true;
                    return;
                }

                if (shouldRestorePerformanceMode)
                {
                    lock (_performanceModeSync)
                    {
                        ApplyPerformanceMode(startupCycleModeKey, persistPreference: false);
                    }
                }

                if (shouldRestoreChargeLimit)
                {
                    _batteryControlService.SetChargeLimitPercentFast(preferredChargeLimitPercent);
                }
            });
            if (skippedBecauseBatterySaverWasAlreadyActive)
            {
                return;
            }

            await Task.Delay(900);
            await Task.Run(() => DecorateBatteryState(_batteryControlService.QueryState()));
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

    private void StartBatteryAutomationMonitor()
    {
        _batteryAutomationTimer ??= new System.Threading.Timer(OnBatteryAutomationTimerTick, null, BatteryAutomationPollIntervalMs, BatteryAutomationPollIntervalMs);
    }

    private void OnBatteryAutomationTimerTick(object? _)
    {
        if (Interlocked.Exchange(ref _batteryAutomationTickInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            EvaluateBatteryAutomation();
        }
        catch
        {
        }
        finally
        {
            Volatile.Write(ref _batteryAutomationTickInProgress, 0);
        }
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

        if (mapping is not null && mapping.Enabled)
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
            var actionOsd = ExecuteAction(mapping.Action, inputEvent);
            if (actionOsd is not null)
            {
                ShowActionOsd(actionOsd);
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
            var actionOsd = ExecuteAction(action, inputEvent: null);
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
        if (target == TouchpadEdgeSlideService.TouchpadEdgeSlideTarget.None)
        {
            return;
        }

        var signedSteps = e.Direction == TouchpadEdgeSlideDirection.Up ? e.Steps : -e.Steps;
        _touchpadEdgeSlideService.Queue(target, signedSteps, BuildTouchpadEdgeSlideSummary(e, target));
    }

    private static bool ShouldExecuteOnBackgroundThread(KeyActionMappingConfiguration mapping)
    {
        return string.Equals(mapping.Action?.Type, HotkeyActionType.CyclePerformanceMode, StringComparison.OrdinalIgnoreCase);
    }

    private ActionExecutionOsd? ExecuteAction(ActionDefinitionConfiguration action, InputEvent? inputEvent)
    {
        switch (action.Type)
        {
            case HotkeyActionType.None:
                return null;
            case HotkeyActionType.CyclePerformanceMode:
                return ExecuteCyclePerformanceModeAction();
            case HotkeyActionType.ShowFnLockOsd:
            case HotkeyActionType.ShowCapsLockOsd:
            case HotkeyActionType.ShowKeyboardBacklightOsd:
                return ResolveBuiltInActionOsd(action.Type, inputEvent?.ReportHex);
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
                return ExecuteToggleTouchpadAction();
            case HotkeyActionType.MicrophoneMuteOn:
                AudioEndpointController.SetCaptureMute(true);
                return ResolveBuiltInActionOsd(action.Type);
            case HotkeyActionType.MicrophoneMuteOff:
                AudioEndpointController.SetCaptureMute(false);
                return ResolveBuiltInActionOsd(action.Type);
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

    private ActionExecutionOsd? ExecuteCyclePerformanceModeAction()
    {
        var state = GetEffectiveBatteryState();
        if (!state.Supported)
        {
            throw new InvalidOperationException(ResourceStringService.GetString("Worker.DeviceNotSupported", "The device does not expose the performance mode controls."));
        }

        if (string.Equals(
            BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(state.SelectedPerformanceModeKey),
            BatteryControlCatalog.Battery,
            StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var nextCycleModeKey = BatteryControlCatalog.GetNextCyclePerformanceModeKey(
            state.PerformanceModeKey,
            state.SelectedPerformanceModeKey,
            state.IsAcPowered,
            _configuration.Preferences.PerformanceModeCycleKeys);
        var (_, actionOsd) = ApplyPerformanceMode(nextCycleModeKey);
        NotifyControllerAboutBatteryMutation();
        return actionOsd ?? throw new InvalidOperationException("Performance mode OSD could not be resolved.");
    }

    private void EvaluateBatteryAutomation()
    {
        var state = GetEffectiveBatteryState();
        if (!state.Supported)
        {
            return;
        }

        var targetModeKey = ResolveAutomaticPerformanceModeSelection(state);
        if (string.IsNullOrWhiteSpace(targetModeKey))
        {
            return;
        }

        var shouldShowOsd = ShouldShowAutomaticPerformanceModeOsd(state, targetModeKey);
        var (_, actionOsd) = ApplyPerformanceMode(targetModeKey, persistPreference: false);
        if (shouldShowOsd && actionOsd is not null)
        {
            ShowActionOsd(actionOsd);
        }

        NotifyControllerAboutBatteryMutation();
    }

    private string? ResolveAutomaticPerformanceModeSelection(BatteryControlState state)
    {
        var normalizedRawModeKey = BatteryControlCatalog.NormalizePerformanceModeKey(state.PerformanceModeKey);
        var normalizedSelectedModeKey = BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(state.SelectedPerformanceModeKey);
        var batteryModeOnDcThresholdPercent = BatteryControlCatalog.NormalizeBatteryModeOnDcThresholdPercent(
            _configuration.Preferences.SwitchToBatteryModeOnDcThresholdPercent);
        var exitBatteryModeOnAcThresholdPercent = BatteryControlCatalog.AutoSwitchAlwaysThreshold;
        var startupCycleModeKey = BatteryControlCatalog.GetDefaultPerformanceModeCycleKey(
            _configuration.Preferences.PerformanceModeCycleKeys);

        if (!state.IsAcPowered &&
            ShouldAutoSwitchOnBattery(state.BatteryLevelPercent, batteryModeOnDcThresholdPercent) &&
            !string.Equals(normalizedSelectedModeKey, BatteryControlCatalog.Battery, StringComparison.OrdinalIgnoreCase))
        {
            return BatteryControlCatalog.Battery;
        }

        if (state.IsAcPowered &&
            ShouldAutoSwitchOnAc(state.BatteryLevelPercent, exitBatteryModeOnAcThresholdPercent) &&
            string.Equals(normalizedSelectedModeKey, BatteryControlCatalog.Battery, StringComparison.OrdinalIgnoreCase))
        {
            return startupCycleModeKey;
        }

        if (!state.IsAcPowered &&
            string.Equals(normalizedRawModeKey, BatteryControlCatalog.Beast, StringComparison.OrdinalIgnoreCase))
        {
            return BatteryControlCatalog.Extreme;
        }

        if (state.IsAcPowered &&
            string.Equals(normalizedRawModeKey, BatteryControlCatalog.Turbo, StringComparison.OrdinalIgnoreCase))
        {
            return BatteryControlCatalog.Extreme;
        }

        return null;
    }

    private static bool ShouldAutoSwitchOnBattery(int batteryLevelPercent, int thresholdPercent)
    {
        if (thresholdPercent == BatteryControlCatalog.AutoSwitchNeverThreshold)
        {
            return false;
        }

        if (thresholdPercent == BatteryControlCatalog.AutoSwitchAlwaysThreshold)
        {
            return true;
        }

        return batteryLevelPercent >= 0 && batteryLevelPercent <= thresholdPercent;
    }

    private static bool ShouldAutoSwitchOnAc(int batteryLevelPercent, int thresholdPercent)
    {
        if (thresholdPercent == BatteryControlCatalog.AutoSwitchNeverThreshold)
        {
            return false;
        }

        if (thresholdPercent == BatteryControlCatalog.AutoSwitchAlwaysThreshold)
        {
            return true;
        }

        return batteryLevelPercent >= 0 && batteryLevelPercent >= thresholdPercent;
    }

    private static bool ShouldShowAutomaticPerformanceModeOsd(BatteryControlState state, string targetModeKey)
    {
        var normalizedCurrentSelectedModeKey = BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(state.SelectedPerformanceModeKey);
        var normalizedTargetModeKey = BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(targetModeKey);
        var isEnteringBatterySaver =
            !string.Equals(normalizedCurrentSelectedModeKey, BatteryControlCatalog.Battery, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normalizedTargetModeKey, BatteryControlCatalog.Battery, StringComparison.OrdinalIgnoreCase);
        var isLeavingBatterySaver =
            string.Equals(normalizedCurrentSelectedModeKey, BatteryControlCatalog.Battery, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalizedTargetModeKey, BatteryControlCatalog.Battery, StringComparison.OrdinalIgnoreCase);
        return isEnteringBatterySaver || isLeavingBatterySaver;
    }

    private ActionExecutionOsd ExecuteToggleTouchpadAction()
    {
        var isEnabled = _nativeActionService.ToggleTouchpad();
        var title = isEnabled switch
        {
            true => ResourceStringService.GetString("Osd.Title.TouchpadOn", "Touchpad on"),
            false => ResourceStringService.GetString("Osd.Title.TouchpadOff", "Touchpad off"),
            _ => ResourceStringService.GetString("Osd.Title.Touchpad", "Touchpad")
        };
        var assetKey = isEnabled switch
        {
            true => BuiltInOsdAsset.TouchpadOn,
            false => BuiltInOsdAsset.TouchpadOff,
            _ => null
        };

        return new ActionExecutionOsd(
            title,
            new IconConfiguration
            {
                Mode = string.IsNullOrWhiteSpace(assetKey) ? IconSourceMode.None : IconSourceMode.CustomFile,
                Path = assetKey
            });
    }

    private async Task RefreshBatteryStateAfterMutationAsync()
    {
        try
        {
            await Task.Delay(700);
            DecorateBatteryState(_batteryControlService.QueryState());
            await NotifyControllerAsync(WorkerNotificationType.Started);
        }
        catch
        {
        }
    }

    private static ActionExecutionOsd? ResolveBuiltInActionOsd(string? actionType, string? reportHex = null)
    {
        if (BuiltInOsdCatalog.ResolveForAction(actionType, reportHex) is not { } osd)
        {
            return null;
        }

        return new ActionExecutionOsd(
            osd.Title,
            new IconConfiguration
            {
                Mode = string.IsNullOrWhiteSpace(osd.AssetKey) ? IconSourceMode.None : IconSourceMode.CustomFile,
                Path = osd.AssetKey
            });
    }

    private void ShowActionOsd(ActionExecutionOsd actionOsd)
    {
        ShowOsd(actionOsd.Title, actionOsd.Icon);
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
            TouchpadGestureTriggerKind.FiveFingerPinchIn => touchpad.FiveFingerPinchInAction,
            TouchpadGestureTriggerKind.FiveFingerPinchOut => touchpad.FiveFingerPinchOutAction,
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
            TouchpadGestureTriggerKind.FiveFingerPinchIn => "Touchpad five-finger pinch in",
            TouchpadGestureTriggerKind.FiveFingerPinchOut => "Touchpad five-finger pinch out",
            TouchpadGestureTriggerKind.DeepPress when !string.IsNullOrWhiteSpace(e.RegionId) => $"Touchpad deep press ({regionLabel})",
            _ => "Touchpad deep press (main region)"
        };
    }

    private TouchpadEdgeSlideService.TouchpadEdgeSlideTarget ResolveEdgeSlideTarget(TouchpadEdgeSlideSide side)
    {
        var action = side == TouchpadEdgeSlideSide.Left
            ? _configuration.Touchpad.LeftEdgeSlideAction
            : _configuration.Touchpad.RightEdgeSlideAction;

        return action?.Type switch
        {
            HotkeyActionType.BrightnessUp or HotkeyActionType.BrightnessDown => TouchpadEdgeSlideService.TouchpadEdgeSlideTarget.Brightness,
            HotkeyActionType.VolumeUp or HotkeyActionType.VolumeDown => TouchpadEdgeSlideService.TouchpadEdgeSlideTarget.Volume,
            _ => TouchpadEdgeSlideService.TouchpadEdgeSlideTarget.None
        };
    }

    private static string BuildTouchpadEdgeSlideSummary(TouchpadEdgeSlideEventArgs e, TouchpadEdgeSlideService.TouchpadEdgeSlideTarget target)
    {
        var sideLabel = e.Side == TouchpadEdgeSlideSide.Left ? "left edge" : "right edge";
        var targetLabel = target == TouchpadEdgeSlideService.TouchpadEdgeSlideTarget.Brightness ? "brightness" : "volume";
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
            }
        };
    }

    private WorkerStatus BuildStatus()
    {
        var batteryState = _batteryControlService.TryGetCachedState(out var cachedBatteryState)
            ? DecorateBatteryState(cachedBatteryState)
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

    private (BatteryControlState State, ActionExecutionOsd? Osd) ApplyPerformanceMode(string? modeKey, bool persistPreference = true)
    {
        lock (_performanceModeSync)
        {
            var state = GetEffectiveBatteryState();
            var normalizedSelectedModeKey = BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(modeKey);
            var normalizedModeKey = BatteryControlCatalog.ResolveRawPerformanceModeKey(normalizedSelectedModeKey, state.IsAcPowered);
            if (!state.Supported)
            {
                throw new InvalidOperationException(ResourceStringService.GetString("Worker.DeviceNotSupported", "The device does not expose the performance mode controls."));
            }

            var updatedState = DecorateBatteryState(_batteryControlService.SetPerformanceModeFast(normalizedModeKey));
            updatedState.SelectedPerformanceModeKey = normalizedSelectedModeKey;
            updatedState.IsBatterySaverEnabled = string.Equals(normalizedModeKey, BatteryControlCatalog.Battery, StringComparison.OrdinalIgnoreCase);
            _windowsPowerModeService.SetActiveScheme(ResolveWindowsPowerSchemeAlias(normalizedModeKey));
            if (persistPreference)
            {
                PersistPreferredPerformanceSelection(normalizedSelectedModeKey);
            }

            var title = BatteryControlCatalog.GetPerformanceModeLabel(updatedState.SelectedPerformanceModeKey);
            var assetKey = BatteryControlCatalog.GetPerformanceModeOsdAssetKey(updatedState.PerformanceModeKey);
            _stateMessage = "Performance mode set to " + title + ".";

            return (
                updatedState,
                new ActionExecutionOsd(
                    title,
                    new IconConfiguration
                    {
                        Mode = string.IsNullOrWhiteSpace(assetKey) ? IconSourceMode.None : IconSourceMode.CustomFile,
                        Path = assetKey
                    }));
        }
    }

    private BatteryControlState GetEffectiveBatteryState()
    {
        var state = _batteryControlService.TryGetCachedState(out var cachedState)
            ? cachedState
            : _batteryControlService.QueryState();
        return DecorateBatteryState(state);
    }

    private static string ResolveWindowsPowerSchemeAlias(string modeKey)
    {
        return BatteryControlCatalog.NormalizePerformanceModeKey(modeKey) switch
        {
            BatteryControlCatalog.Battery or BatteryControlCatalog.Silent => WindowsPowerModeService.PowerSaverSchemeAlias,
            BatteryControlCatalog.Smart => WindowsPowerModeService.BalancedSchemeAlias,
            BatteryControlCatalog.Turbo or BatteryControlCatalog.Beast => WindowsPowerModeService.HighPerformanceSchemeAlias,
            _ => WindowsPowerModeService.BalancedSchemeAlias
        };
    }

    private BatteryControlState DecorateBatteryState(BatteryControlState state)
    {
        state.IsAcPowered = _windowsPowerModeService.IsAcPowered();
        state.BatteryLevelPercent = _windowsPowerModeService.GetBatteryLevelPercent();
        state.IsBatterySaverEnabled = string.Equals(
            BatteryControlCatalog.NormalizePerformanceModeKey(state.PerformanceModeKey),
            BatteryControlCatalog.Battery,
            StringComparison.OrdinalIgnoreCase);
        state.SelectedPerformanceModeKey = BatteryControlCatalog.ResolveSelectedPerformanceModeKey(
            state.PerformanceModeKey,
            _configuration.Preferences.PreferredPerformanceModeKey);
        return state;
    }

    private void PersistPreferredPerformanceSelection(string selectedModeKey)
    {
        _configuration.Preferences.PreferredPerformanceModeKey =
            BatteryControlCatalog.NormalizeSelectedPerformanceModeKey(selectedModeKey);
        _configService.Save(_configuration);
    }

    private void NotifyControllerAboutBatteryMutation()
    {
        _ = NotifyControllerAsync(WorkerNotificationType.Started);
        _ = Task.Run(RefreshBatteryStateAfterMutationAsync);
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
