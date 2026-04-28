using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using MeowBox.Core.Models;

namespace MeowBox.Worker.Services;

internal sealed class TouchpadInputService : NativeWindow, IDisposable
{
    private static readonly TimeSpan StaleInteractionTimeout = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan SessionResetThreshold = TimeSpan.FromMilliseconds(350);
    private const double EdgeRegionRatio = 0.06d;
    private const double EdgeStartPercent = 6d;
    private const double EdgeStepPercent = 2d;
    private const int PinchStartCandidateFrames = 2;
    private const int PinchTriggerCandidateFramesRequired = 2;
    private const double PinchInTriggerRelativeSpreadRatio = 0.84d;
    private const double PinchOutTriggerRelativeSpreadRatio = 1.16d;
    private const double PinchTriggerAbsoluteTravelRatio = 0.06d;
    private const double PinchCentroidTravelRatio = 0.10d;

    private readonly object _sync = new();
    private readonly Dictionary<nint, RawInputDeviceSnapshot> _deviceCache = [];
    private static readonly RawInputDeviceSnapshot FallbackTouchpadDevice = new(0, 0x0D, 0x05, "Precision touchpad", string.Empty, true);
    private readonly System.Threading.Timer _staleStateTimer;

    private TouchpadTrackingContext _context = new();
    private TouchpadLiveStateSnapshot _latestState = new()
    {
        LightPressThreshold = RuntimeDefaults.DefaultTouchpadLightPressThreshold,
        DeepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold
    };

    private int _lightPressThreshold = RuntimeDefaults.DefaultTouchpadLightPressThreshold;
    private int _deepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold;
    private int _longPressDurationMs = RuntimeDefaults.DefaultTouchpadCornerLongPressDurationMs;
    private int _surfaceWidth = RuntimeDefaults.DefaultTouchpadSurfaceWidth;
    private int _surfaceHeight = RuntimeDefaults.DefaultTouchpadSurfaceHeight;
    private bool _edgeSlideEnabled;
    private TouchpadRegionBoundsConfiguration _leftTopBounds = TouchpadCornerRegionConfiguration.CreateLeftTopDefault().Bounds;
    private TouchpadRegionBoundsConfiguration _rightTopBounds = TouchpadCornerRegionConfiguration.CreateRightTopDefault().Bounds;
    private DateTimeOffset _lastFrameAt;
    private readonly NativeMethods.LowLevelMouseProc _mouseHookProc;
    private nint _mouseHookHandle;
    private volatile bool _suppressMouseMovement;
    private bool _disposed;

    public TouchpadInputService()
    {
        _mouseHookProc = MouseHookCallback;
        CreateHandle(new CreateParams
        {
            Caption = "MeowBox.TouchpadInput",
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0,
            Style = unchecked((int)0x80000000),
            ExStyle = 0x00000080
        });

        RegisterTouchpadRawInput();
        _staleStateTimer = new System.Threading.Timer(OnStaleStateTimerTick, null, 50, 50);
    }

    public event EventHandler<TouchpadGestureTriggerEventArgs>? GestureTriggered;
    public event EventHandler<TouchpadEdgeSlideEventArgs>? EdgeSlideTriggered;
    public event EventHandler<TouchpadLiveStateSnapshot>? StateChanged;

    public void UpdateConfiguration(TouchpadConfiguration? configuration)
    {
        configuration ??= new TouchpadConfiguration();
        var edgeSlideEnabled = HasAssignedAction(configuration.LeftEdgeSlideAction) ||
                               HasAssignedAction(configuration.RightEdgeSlideAction) ||
                               configuration.EdgeSlideEnabled;

        lock (_sync)
        {
            _lightPressThreshold = Math.Clamp(
                configuration.LightPressThreshold,
                20,
                RuntimeDefaults.DefaultTouchpadDeepPressThreshold - 1);
            _deepPressThreshold = Math.Clamp(
                configuration.DeepPressThreshold,
                RuntimeDefaults.DefaultTouchpadDeepPressThreshold,
                RuntimeDefaults.DefaultTouchpadDeepPressThreshold);
            _longPressDurationMs = Math.Clamp(
                configuration.LongPressDurationMs,
                200,
                3000);
            _surfaceWidth = Math.Max(1, configuration.SurfaceWidth);
            _surfaceHeight = Math.Max(1, configuration.SurfaceHeight);

            var edgeSlideStateChanged = _edgeSlideEnabled != edgeSlideEnabled;
            _edgeSlideEnabled = edgeSlideEnabled;
            _leftTopBounds = CloneBounds(configuration.LeftTopCorner?.Bounds, TouchpadCornerRegionConfiguration.CreateLeftTopDefault().Bounds);
            _rightTopBounds = CloneBounds(configuration.RightTopCorner?.Bounds, TouchpadCornerRegionConfiguration.CreateRightTopDefault().Bounds);
            _latestState.LightPressThreshold = _lightPressThreshold;
            _latestState.DeepPressThreshold = _deepPressThreshold;

            if (edgeSlideStateChanged)
            {
                ResetEdgeSlide();
            }
        }

        if (edgeSlideEnabled)
        {
            InstallMouseHook();
        }
        else
        {
            UninstallMouseHook();
        }

        StateChanged?.Invoke(this, GetLatestState());
    }

    public TouchpadLiveStateSnapshot GetLatestState()
    {
        lock (_sync)
        {
            return CloneState(_latestState);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _staleStateTimer.Dispose();
        UninstallMouseHook();
        DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WmInput:
                ProcessRawInput(m.LParam);
                break;
            case NativeMethods.WmInputDeviceChange:
                lock (_sync)
                {
                    _deviceCache.Clear();
                }

                break;
        }

        base.WndProc(ref m);
    }

    private void RegisterTouchpadRawInput()
    {
        var registration = new NativeMethods.RAWINPUTDEVICE
        {
            usUsagePage = 0x0D,
            usUsage = 0x05,
            dwFlags = NativeMethods.RidevInputSink | NativeMethods.RidevDevNotify,
            hwndTarget = Handle
        };

        var registered = NativeMethods.RegisterRawInputDevices([registration], 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
        lock (_sync)
        {
            _latestState.IsRegistered = registered;
        }
    }

    private void ProcessRawInput(nint rawInputHandle)
    {
        uint size = 0;
        _ = NativeMethods.GetRawInputData(rawInputHandle, NativeMethods.RidInput, 0, ref size, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>());
        if (size == 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (NativeMethods.GetRawInputData(rawInputHandle, NativeMethods.RidInput, buffer, ref size, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>()) == uint.MaxValue)
            {
                return;
            }

            var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);
            if (header.dwType != NativeMethods.RimTypeHid)
            {
                return;
            }

            var device = GetOrCreateDeviceSnapshot(header.hDevice);
            if (!device.IsTouchpad)
            {
                return;
            }

            var bodyPtr = buffer + Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
            var hidHeader = Marshal.PtrToStructure<NativeMethods.RAWHIDHEADER>(bodyPtr);
            var rawDataLength = checked((int)(hidHeader.dwSizeHid * hidHeader.dwCount));
            if (rawDataLength <= 0)
            {
                return;
            }

            var rawDataPtr = bodyPtr + Marshal.SizeOf<NativeMethods.RAWHIDHEADER>();
            var rawData = new byte[rawDataLength];
            Marshal.Copy(rawDataPtr, rawData, 0, rawDataLength);

            var reportCount = hidHeader.dwSizeHid == 0 ? 0 : rawData.Length / (int)hidHeader.dwSizeHid;
            for (var reportIndex = 0; reportIndex < reportCount; reportIndex++)
            {
                var report = new byte[hidHeader.dwSizeHid];
                Array.Copy(rawData, reportIndex * hidHeader.dwSizeHid, report, 0, (int)hidHeader.dwSizeHid);
                ProcessTouchpadReport(device, report);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void ProcessTouchpadReport(RawInputDeviceSnapshot device, byte[] report)
    {
        var parsed = TouchpadDecoder.TryParse(report);
        var timestamp = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            _lastFrameAt = timestamp;
        }

        if (parsed is null)
        {
            lock (_sync)
            {
                var shouldKeepPreviousLiveState = _latestState.HasReceivedInput &&
                                                  _latestState.SupportsPressure &&
                                                  (_latestState.HasInteraction ||
                                                   _latestState.ButtonPressed ||
                                                   _latestState.ContactCount > 0 ||
                                                   _latestState.Contacts.Count > 0) &&
                                                  timestamp - _latestState.Timestamp <= TimeSpan.FromMilliseconds(220);

                _latestState = shouldKeepPreviousLiveState
                    ? CloneState(_latestState)
                    : new TouchpadLiveStateSnapshot
                    {
                        IsRegistered = _latestState.IsRegistered,
                        HasReceivedInput = true,
                        SupportsPressure = false,
                        Timestamp = timestamp,
                        DeviceName = device.DisplayName,
                        HasInteraction = false,
                        ButtonPressed = false,
                        DeepPressed = false,
                        Pressure = 0,
                        PeakPressure = _context.PeakPressure,
                        LightPressThreshold = _lightPressThreshold,
                        DeepPressThreshold = _deepPressThreshold,
                        Contacts = []
                    };

                _latestState.Timestamp = timestamp;
                _latestState.DeviceName = device.DisplayName;
            }

            StateChanged?.Invoke(this, GetLatestState());
            return;
        }

        var pressure = TouchpadDecoder.GetCurrentPressure(parsed);
        var hasInteraction = TouchpadDecoder.HasInteraction(parsed);
        var activeContacts = parsed.Contacts
            .Where(static contact => contact.Tip || contact.Confidence)
            .ToList();
        var contacts = activeContacts
            .Select(static contact => new TouchpadLiveContactSnapshot
            {
                SlotIndex = contact.SlotIndex,
                Tip = contact.Tip,
                Confidence = contact.Confidence,
                ContactId = contact.ContactId,
                X = contact.X,
                Y = contact.Y,
                Pressure = contact.Pressure
            })
            .ToList();

        TouchpadGestureTriggerEventArgs? gestureTriggered = null;
        TouchpadEdgeSlideEventArgs? edgeSlideTriggered = null;
        lock (_sync)
        {
            var primaryContact = GetPrimaryContact(activeContacts);
            var currentRegionId = ResolveRegionId(primaryContact);
            var threshold = _deepPressThreshold;
            var lightPressThreshold = _lightPressThreshold;
            if (hasInteraction && (!_context.HasInteraction || timestamp - _context.LastActiveAt > SessionResetThreshold))
            {
                _context = CreateTrackingContext(timestamp);
            }

            gestureTriggered = ProcessSingleGestureSession(
                activeContacts,
                primaryContact,
                currentRegionId,
                parsed.Button1,
                hasInteraction,
                pressure,
                timestamp);

            _context.LastPressure = pressure;
            _context.LastButtonPressed = parsed.Button1;
            _context.HasInteraction = hasInteraction;
            if (hasInteraction)
            {
                _context.LastActiveAt = timestamp;
                _context.PeakPressure = Math.Max(_context.PeakPressure, pressure);
            }

            edgeSlideTriggered = ProcessEdgeSlide(primaryContact, contacts, hasInteraction, pressure);

            _latestState = new TouchpadLiveStateSnapshot
            {
                IsRegistered = _latestState.IsRegistered,
                HasReceivedInput = true,
                SupportsPressure = true,
                Timestamp = timestamp,
                DeviceName = device.DisplayName,
                HasInteraction = hasInteraction,
                ButtonPressed = parsed.Button1,
                DeepPressed = _context.DeepPressed,
                Pressure = pressure,
                PeakPressure = _context.PeakPressure,
                LightPressThreshold = lightPressThreshold,
                DeepPressThreshold = threshold,
                ScanTime = parsed.ScanTime,
                ContactCount = parsed.ContactCount,
                Contacts = contacts
            };
        }

        if (gestureTriggered is not null)
        {
            GestureTriggered?.Invoke(this, gestureTriggered);
        }

        if (edgeSlideTriggered is not null)
        {
            EdgeSlideTriggered?.Invoke(this, edgeSlideTriggered);
        }

        StateChanged?.Invoke(this, GetLatestState());
    }

    private void OnStaleStateTimerTick(object? state)
    {
        TouchpadLiveStateSnapshot? snapshotToBroadcast = null;

        lock (_sync)
        {
            if (_lastFrameAt == default)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (now - _lastFrameAt < StaleInteractionTimeout)
            {
                return;
            }

            var shouldClear = _latestState.HasInteraction ||
                              _latestState.ButtonPressed ||
                              _latestState.DeepPressed ||
                              _latestState.Pressure > 0 ||
                              _latestState.ContactCount > 0 ||
                              _latestState.Contacts.Count > 0;
            if (!shouldClear)
            {
                return;
            }

            _context = new TouchpadTrackingContext
            {
                LastActiveAt = now
            };
            _suppressMouseMovement = false;

            _latestState = new TouchpadLiveStateSnapshot
            {
                IsRegistered = _latestState.IsRegistered,
                HasReceivedInput = true,
                SupportsPressure = _latestState.SupportsPressure,
                Timestamp = now,
                DeviceName = _latestState.DeviceName,
                HasInteraction = false,
                ButtonPressed = false,
                DeepPressed = false,
                Pressure = 0,
                PeakPressure = 0,
                LightPressThreshold = _lightPressThreshold,
                DeepPressThreshold = _deepPressThreshold,
                ScanTime = 0,
                ContactCount = 0,
                Contacts = []
            };

            snapshotToBroadcast = CloneState(_latestState);
        }

        if (snapshotToBroadcast is not null)
        {
            StateChanged?.Invoke(this, snapshotToBroadcast);
        }
    }

    private RawInputDeviceSnapshot GetOrCreateDeviceSnapshot(nint deviceHandle)
    {
        if (deviceHandle == 0)
        {
            return FallbackTouchpadDevice;
        }

        lock (_sync)
        {
            if (_deviceCache.TryGetValue(deviceHandle, out var cached))
            {
                return cached;
            }

            var devicePath = GetDevicePath(deviceHandle);
            var info = GetDeviceInfo(deviceHandle);
            if (info is null)
            {
                return new RawInputDeviceSnapshot(deviceHandle, 0x0D, 0x05, "Precision touchpad", devicePath, true);
            }

            var snapshot = new RawInputDeviceSnapshot(
                deviceHandle,
                info.Value.Anonymous.hid.usUsagePage,
                info.Value.Anonymous.hid.usUsage,
                BuildDisplayName(devicePath, info.Value),
                devicePath,
                IsTouchpad(info.Value, devicePath));

            _deviceCache[deviceHandle] = snapshot;
            return snapshot;
        }
    }

    private static string BuildDisplayName(string devicePath, NativeMethods.RID_DEVICE_INFO info)
    {
        var suffix = $"[page 0x{info.Anonymous.hid.usUsagePage:X4}, usage 0x{info.Anonymous.hid.usUsage:X4}]";
        return string.IsNullOrWhiteSpace(devicePath)
            ? suffix
            : $"{devicePath} {suffix}";
    }

    private static bool IsTouchpad(NativeMethods.RID_DEVICE_INFO info, string devicePath)
    {
        if (info.dwType == NativeMethods.RimTypeHid &&
            info.Anonymous.hid.usUsagePage == 0x0D &&
            info.Anonymous.hid.usUsage == 0x05)
        {
            return true;
        }

        return devicePath.Contains("touchpad", StringComparison.OrdinalIgnoreCase) ||
               devicePath.Contains("touch pad", StringComparison.OrdinalIgnoreCase);
    }

    private static TouchpadLiveStateSnapshot CloneState(TouchpadLiveStateSnapshot source)
    {
        return new TouchpadLiveStateSnapshot
        {
            IsRegistered = source.IsRegistered,
            HasReceivedInput = source.HasReceivedInput,
            SupportsPressure = source.SupportsPressure,
            Timestamp = source.Timestamp,
            DeviceName = source.DeviceName,
            HasInteraction = source.HasInteraction,
            ButtonPressed = source.ButtonPressed,
            DeepPressed = source.DeepPressed,
            Pressure = source.Pressure,
            PeakPressure = source.PeakPressure,
            LightPressThreshold = source.LightPressThreshold,
            DeepPressThreshold = source.DeepPressThreshold,
            ScanTime = source.ScanTime,
            ContactCount = source.ContactCount,
            Contacts = source.Contacts
                .Select(static contact => new TouchpadLiveContactSnapshot
                {
                    SlotIndex = contact.SlotIndex,
                    Tip = contact.Tip,
                    Confidence = contact.Confidence,
                    ContactId = contact.ContactId,
                    X = contact.X,
                    Y = contact.Y,
                    Pressure = contact.Pressure
                })
                .ToList()
        };
    }

    private static NativeMethods.RID_DEVICE_INFO? GetDeviceInfo(nint deviceHandle)
    {
        var size = (uint)Marshal.SizeOf<NativeMethods.RID_DEVICE_INFO>();
        var info = new NativeMethods.RID_DEVICE_INFO { cbSize = size };
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            if (NativeMethods.GetRawInputDeviceInfo(deviceHandle, NativeMethods.RidiDeviceInfo, buffer, ref size) == uint.MaxValue)
            {
                return null;
            }

            return Marshal.PtrToStructure<NativeMethods.RID_DEVICE_INFO>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string GetDevicePath(nint deviceHandle)
    {
        uint charCount = 0;
        _ = NativeMethods.GetRawInputDeviceInfo(deviceHandle, NativeMethods.RidiDeviceName, new StringBuilder(), ref charCount);
        if (charCount == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder((int)charCount);
        return NativeMethods.GetRawInputDeviceInfo(deviceHandle, NativeMethods.RidiDeviceName, builder, ref charCount) == uint.MaxValue
            ? string.Empty
            : builder.ToString();
    }

    private sealed record RawInputDeviceSnapshot(nint Handle, ushort UsagePage, ushort Usage, string DisplayName, string DevicePath, bool IsTouchpad);

    private TouchpadTrackingContext CreateTrackingContext(DateTimeOffset timestamp)
    {
        return new TouchpadTrackingContext
        {
            LastActiveAt = timestamp
        };
    }

    private TouchpadGestureTriggerEventArgs? ProcessSingleGestureSession(
        IReadOnlyList<TouchpadDecodedContact> activeContacts,
        TouchpadDecodedContact? primaryContact,
        string? currentRegionId,
        bool buttonPressed,
        bool hasInteraction,
        int pressure,
        DateTimeOffset timestamp)
    {
        if (!hasInteraction)
        {
            ResetSingleGestureSession(timestamp, pressure);
            return null;
        }

        if (_context.GestureSessionOwner == TouchpadGestureSessionOwner.None)
        {
            if (IsFiveFingerPinchCandidate(activeContacts, buttonPressed))
            {
                _context.FiveFingerPinchReadyFrames++;
                if (_context.FiveFingerPinchReadyFrames >= PinchStartCandidateFrames)
                {
                    StartFiveFingerPinchSession(activeContacts, pressure, timestamp);
                }

                return null;
            }

            _context.FiveFingerPinchReadyFrames = 0;

            var gestureRearmPressureThreshold = Math.Max(20, _lightPressThreshold - 20);
            if (buttonPressed && pressure > gestureRearmPressureThreshold)
            {
                StartPressGestureSession(primaryContact, currentRegionId, pressure, timestamp);
            }

            return null;
        }

        if (_context.GestureSessionPhase == TouchpadGestureSessionPhase.Triggered)
        {
            if (_context.GestureSessionOwner == TouchpadGestureSessionOwner.Press)
            {
                UpdateTriggeredPressGestureState(buttonPressed, hasInteraction, pressure);
            }

            return null;
        }

        return _context.GestureSessionOwner switch
        {
            TouchpadGestureSessionOwner.Press => ProcessPressGestureSession(
                primaryContact,
                currentRegionId,
                buttonPressed,
                hasInteraction,
                pressure,
                timestamp),
            TouchpadGestureSessionOwner.FiveFingerPinch => ProcessFiveFingerPinchSession(
                activeContacts,
                buttonPressed,
                pressure,
                timestamp),
            _ => null
        };
    }

    private TouchpadGestureTriggerEventArgs? ProcessPressGestureSession(
        TouchpadDecodedContact? primaryContact,
        string? currentRegionId,
        bool buttonPressed,
        bool hasInteraction,
        int pressure,
        DateTimeOffset timestamp)
    {
        var threshold = _deepPressThreshold;
        var lightPressThreshold = _lightPressThreshold;
        var gestureRearmPressureThreshold = Math.Max(20, lightPressThreshold - 20);
        if (!buttonPressed || pressure <= gestureRearmPressureThreshold)
        {
            ResetSingleGestureSession(timestamp, pressure);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(currentRegionId) &&
            string.IsNullOrWhiteSpace(_context.SessionRegionId))
        {
            _context.SessionRegionId = currentRegionId;
            _context.StartX = primaryContact?.X ?? _context.StartX;
            _context.StartY = primaryContact?.Y ?? _context.StartY;
        }

        var pressureDelta = pressure - _context.LastPressure;
        var pressStartPressure = _context.PressStartPressure > 0 ? _context.PressStartPressure : pressure;
        var aboveDeepThreshold = pressure >= threshold &&
                                 pressure - pressStartPressure >= 20;
        var deepCandidateFrames = aboveDeepThreshold ? _context.DeepCandidateFrames + 1 : 0;
        var isEdgeSideContact = primaryContact is not null &&
                                ResolveEdgeSlideSide(primaryContact.Value, excludeCornerRegions: true) is not null;
        if (!_context.DeepPressed &&
            deepCandidateFrames >= 2 &&
            (!string.IsNullOrWhiteSpace(_context.SessionRegionId) || !isEdgeSideContact))
        {
            _context.DeepPressed = true;
            _context.GestureSessionPhase = TouchpadGestureSessionPhase.Triggered;
            _context.LongPressStartedAt = default;
            _context.DeepCandidateFrames = deepCandidateFrames;
            return new TouchpadGestureTriggerEventArgs(
                TouchpadGestureTriggerKind.DeepPress,
                _context.SessionRegionId,
                _context.StartX,
                _context.StartY,
                pressure);
        }

        var releaseFloor = Math.Max(gestureRearmPressureThreshold, threshold - 40);
        if (_context.DeepPressed &&
            (!buttonPressed || !hasInteraction || pressure <= releaseFloor || pressureDelta <= -25))
        {
            _context.DeepPressed = false;
            deepCandidateFrames = 0;
        }

        if (!string.IsNullOrWhiteSpace(_context.SessionRegionId))
        {
            if (_context.LongPressStartedAt == default &&
                pressure >= lightPressThreshold)
            {
                _context.LongPressStartedAt = timestamp;
            }

            if (_context.LongPressStartedAt != default &&
                timestamp - _context.LongPressStartedAt >= TimeSpan.FromMilliseconds(_longPressDurationMs))
            {
                _context.GestureSessionPhase = TouchpadGestureSessionPhase.Triggered;
                _context.DeepCandidateFrames = deepCandidateFrames;
                return new TouchpadGestureTriggerEventArgs(
                    TouchpadGestureTriggerKind.LongPress,
                    _context.SessionRegionId,
                    _context.StartX,
                    _context.StartY,
                    pressure);
            }
        }

        _context.DeepCandidateFrames = deepCandidateFrames;
        return null;
    }

    private TouchpadGestureTriggerEventArgs? ProcessFiveFingerPinchSession(
        IReadOnlyList<TouchpadDecodedContact> activeContacts,
        bool buttonPressed,
        int pressure,
        DateTimeOffset timestamp)
    {
        if (!IsFiveFingerPinchCandidate(activeContacts, buttonPressed))
        {
            ResetSingleGestureSession(timestamp, pressure);
            return null;
        }

        var metrics = CalculateContactMetrics(activeContacts);
        _context.FiveFingerPinchMaxObservedSpread = Math.Max(_context.FiveFingerPinchMaxObservedSpread, metrics.Spread);
        _context.FiveFingerPinchMinObservedSpread = Math.Min(_context.FiveFingerPinchMinObservedSpread, metrics.Spread);
        var pinchInBaselineSpread = Math.Max(_context.FiveFingerPinchBaselineSpread, _context.FiveFingerPinchMaxObservedSpread);
        var pinchOutBaselineSpread = _context.FiveFingerPinchMinObservedSpread <= 0
            ? _context.FiveFingerPinchBaselineSpread
            : Math.Min(_context.FiveFingerPinchBaselineSpread, _context.FiveFingerPinchMinObservedSpread);
        var minimumSurfaceDimension = Math.Min(_surfaceWidth, _surfaceHeight);
        var contraction = pinchInBaselineSpread - metrics.Spread;
        var expansion = metrics.Spread - pinchOutBaselineSpread;
        var centroidTravel = CalculateDistance(
            _context.FiveFingerPinchStartCentroidX,
            _context.FiveFingerPinchStartCentroidY,
            metrics.CentroidX,
            metrics.CentroidY);
        var staysCentered = centroidTravel <= minimumSurfaceDimension * PinchCentroidTravelRatio;
        var meetsPinchInThreshold = staysCentered &&
                                    metrics.Spread <= pinchInBaselineSpread * PinchInTriggerRelativeSpreadRatio &&
                                    contraction >= minimumSurfaceDimension * PinchTriggerAbsoluteTravelRatio;
        var meetsPinchOutThreshold = staysCentered &&
                                     metrics.Spread >= pinchOutBaselineSpread * PinchOutTriggerRelativeSpreadRatio &&
                                     expansion >= minimumSurfaceDimension * PinchTriggerAbsoluteTravelRatio;
        _context.FiveFingerPinchInTriggerFrames = meetsPinchInThreshold
            ? _context.FiveFingerPinchInTriggerFrames + 1
            : 0;
        _context.FiveFingerPinchOutTriggerFrames = meetsPinchOutThreshold
            ? _context.FiveFingerPinchOutTriggerFrames + 1
            : 0;
        if (_context.FiveFingerPinchInTriggerFrames >= PinchTriggerCandidateFramesRequired)
        {
            _context.GestureSessionPhase = TouchpadGestureSessionPhase.Triggered;
            return new TouchpadGestureTriggerEventArgs(
                TouchpadGestureTriggerKind.FiveFingerPinchIn,
                null,
                (int)Math.Round(_context.FiveFingerPinchStartCentroidX),
                (int)Math.Round(_context.FiveFingerPinchStartCentroidY),
                pressure);
        }

        if (_context.FiveFingerPinchOutTriggerFrames < PinchTriggerCandidateFramesRequired)
        {
            return null;
        }

        _context.GestureSessionPhase = TouchpadGestureSessionPhase.Triggered;
        return new TouchpadGestureTriggerEventArgs(
            TouchpadGestureTriggerKind.FiveFingerPinchOut,
            null,
            (int)Math.Round(_context.FiveFingerPinchStartCentroidX),
            (int)Math.Round(_context.FiveFingerPinchStartCentroidY),
            pressure);
    }

    private void UpdateTriggeredPressGestureState(bool buttonPressed, bool hasInteraction, int pressure)
    {
        var gestureRearmPressureThreshold = Math.Max(20, _lightPressThreshold - 20);
        var releaseFloor = Math.Max(gestureRearmPressureThreshold, _deepPressThreshold - 40);
        if (_context.DeepPressed &&
            (!buttonPressed || !hasInteraction || pressure <= releaseFloor || pressure - _context.LastPressure <= -25))
        {
            _context.DeepPressed = false;
        }
    }

    private void StartPressGestureSession(
        TouchpadDecodedContact? primaryContact,
        string? currentRegionId,
        int pressure,
        DateTimeOffset timestamp)
    {
        ResetPressGestureTracking();
        ResetFiveFingerPinchTracking();
        _context.GestureSessionOwner = TouchpadGestureSessionOwner.Press;
        _context.GestureSessionPhase = TouchpadGestureSessionPhase.Tracking;
        _context.PressStartPressure = pressure;
        _context.PeakPressure = pressure;
        _context.InteractionStartedAt = timestamp;
        _context.SessionRegionId = currentRegionId;
        _context.StartX = primaryContact?.X ?? 0;
        _context.StartY = primaryContact?.Y ?? 0;
    }

    private void StartFiveFingerPinchSession(
        IReadOnlyList<TouchpadDecodedContact> activeContacts,
        int pressure,
        DateTimeOffset timestamp)
    {
        var metrics = CalculateContactMetrics(activeContacts);
        ResetPressGestureTracking();
        ResetFiveFingerPinchTracking();
        _context.GestureSessionOwner = TouchpadGestureSessionOwner.FiveFingerPinch;
        _context.GestureSessionPhase = TouchpadGestureSessionPhase.Tracking;
        _context.PeakPressure = pressure;
        _context.InteractionStartedAt = timestamp;
        _context.StartX = (int)Math.Round(metrics.CentroidX);
        _context.StartY = (int)Math.Round(metrics.CentroidY);
        _context.FiveFingerPinchStartCentroidX = metrics.CentroidX;
        _context.FiveFingerPinchStartCentroidY = metrics.CentroidY;
        _context.FiveFingerPinchBaselineSpread = metrics.Spread;
        _context.FiveFingerPinchMaxObservedSpread = metrics.Spread;
        _context.FiveFingerPinchMinObservedSpread = metrics.Spread;
    }

    private void ResetSingleGestureSession(DateTimeOffset timestamp, int pressure)
    {
        _context.GestureSessionOwner = TouchpadGestureSessionOwner.None;
        _context.GestureSessionPhase = TouchpadGestureSessionPhase.Idle;
        _context.PeakPressure = pressure;
        _context.InteractionStartedAt = timestamp;
        ResetPressGestureTracking();
        ResetFiveFingerPinchTracking();
        if (!_edgeSlideEnabled)
        {
            ResetEdgeSlide();
        }
    }

    private void ResetPressGestureTracking()
    {
        _context.PressStartPressure = 0;
        _context.DeepPressed = false;
        _context.LongPressStartedAt = default;
        _context.DeepCandidateFrames = 0;
        _context.SessionRegionId = null;
        _context.StartX = 0;
        _context.StartY = 0;
    }

    private void ResetFiveFingerPinchTracking()
    {
        _context.FiveFingerPinchReadyFrames = 0;
        _context.FiveFingerPinchInTriggerFrames = 0;
        _context.FiveFingerPinchOutTriggerFrames = 0;
        _context.FiveFingerPinchStartCentroidX = 0;
        _context.FiveFingerPinchStartCentroidY = 0;
        _context.FiveFingerPinchBaselineSpread = 0;
        _context.FiveFingerPinchMaxObservedSpread = 0;
        _context.FiveFingerPinchMinObservedSpread = 0;
    }

    private static bool IsFiveFingerPinchCandidate(IReadOnlyList<TouchpadDecodedContact> activeContacts, bool buttonPressed)
    {
        return !buttonPressed && activeContacts.Count == 5;
    }

    private static TouchpadContactMetrics CalculateContactMetrics(IReadOnlyList<TouchpadDecodedContact> activeContacts)
    {
        if (activeContacts.Count == 0)
        {
            return default;
        }

        var centroidX = activeContacts.Average(static contact => contact.X);
        var centroidY = activeContacts.Average(static contact => contact.Y);
        var spread = activeContacts.Average(contact => CalculateDistance(contact.X, contact.Y, centroidX, centroidY));
        return new TouchpadContactMetrics(centroidX, centroidY, spread);
    }

    private static double CalculateDistance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private TouchpadEdgeSlideEventArgs? ProcessEdgeSlide(
        TouchpadDecodedContact? primaryContact,
        IReadOnlyList<TouchpadLiveContactSnapshot> contacts,
        bool hasInteraction,
        int pressure)
    {
        if (!_edgeSlideEnabled)
        {
            ResetEdgeSlide();
            return null;
        }

        if (primaryContact is null || contacts.Count != 1 || !hasInteraction || pressure < _lightPressThreshold)
        {
            ResetEdgeSlide();
            return null;
        }

        var contact = primaryContact.Value;
        var side = ResolveEdgeSlideSide(contact, excludeCornerRegions: true);
        if (side is null)
        {
            ResetEdgeSlide();
            return null;
        }

        if (!_context.EdgeSlideActive ||
            _context.EdgeSlideSide != side ||
            _context.EdgeSlideContactId != contact.ContactId)
        {
            _context.EdgeSlideActive = true;
            _context.EdgeSlideSide = side;
            _context.EdgeSlideContactId = contact.ContactId;
            _context.EdgeSlideStartY = contact.Y;
            _context.EdgeSlideAnchorY = contact.Y;
            _context.EdgeSlideTriggered = false;
            _suppressMouseMovement = true;
            return null;
        }

        var stepSize = _surfaceHeight * EdgeStepPercent / 100d;
        var startSize = _surfaceHeight * EdgeStartPercent / 100d;
        var delta = _context.EdgeSlideAnchorY - contact.Y;
        var steps = 0;
        TouchpadEdgeSlideDirection direction;

        if (!_context.EdgeSlideTriggered)
        {
            var totalDelta = _context.EdgeSlideStartY - contact.Y;
            var travel = Math.Abs(totalDelta);
            if (travel < startSize)
            {
                return null;
            }

            direction = totalDelta > 0 ? TouchpadEdgeSlideDirection.Up : TouchpadEdgeSlideDirection.Down;
            steps = 1 + (int)((travel - startSize) / stepSize);
            var consumedDistance = startSize + ((steps - 1) * stepSize);
            _context.EdgeSlideAnchorY = _context.EdgeSlideStartY + (direction == TouchpadEdgeSlideDirection.Up
                ? -consumedDistance
                : consumedDistance);
            _context.EdgeSlideTriggered = true;
        }
        else
        {
            steps = (int)(Math.Abs(delta) / stepSize);
            if (steps <= 0)
            {
                return null;
            }

            direction = delta > 0 ? TouchpadEdgeSlideDirection.Up : TouchpadEdgeSlideDirection.Down;
            _context.EdgeSlideAnchorY += direction == TouchpadEdgeSlideDirection.Up
                ? -(steps * stepSize)
                : steps * stepSize;
        }

        if (steps <= 0)
        {
            return null;
        }

        return new TouchpadEdgeSlideEventArgs(
            side.Value,
            direction,
            steps,
            contact.X,
            contact.Y,
            contact.Pressure);
    }

    private TouchpadEdgeSlideSide? ResolveEdgeSlideSide(TouchpadDecodedContact contact, bool excludeCornerRegions)
    {
        if (excludeCornerRegions &&
            (IsWithinCornerRegion(contact.X, contact.Y, TouchpadCornerRegionId.LeftTop, _leftTopBounds) ||
             IsWithinCornerRegion(contact.X, contact.Y, TouchpadCornerRegionId.RightTop, _rightTopBounds)))
        {
            return null;
        }

        var leftMax = _surfaceWidth * EdgeRegionRatio;
        var rightMin = _surfaceWidth * (1d - EdgeRegionRatio);

        if (contact.X <= leftMax)
        {
            return TouchpadEdgeSlideSide.Left;
        }

        if (contact.X >= rightMin)
        {
            return TouchpadEdgeSlideSide.Right;
        }

        return null;
    }

    private void ResetEdgeSlide()
    {
        _context.EdgeSlideActive = false;
        _context.EdgeSlideSide = null;
        _context.EdgeSlideContactId = -1;
        _context.EdgeSlideStartY = 0;
        _context.EdgeSlideAnchorY = 0;
        _context.EdgeSlideTriggered = false;
        _suppressMouseMovement = false;
    }

    private void InstallMouseHook()
    {
        if (_mouseHookHandle != 0)
        {
            return;
        }

        _mouseHookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseHookProc,
            NativeMethods.GetModuleHandle(null),
            0);
    }

    private void UninstallMouseHook()
    {
        if (_mouseHookHandle == 0)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = 0;
    }

    private nint MouseHookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 &&
            _suppressMouseMovement &&
            wParam == NativeMethods.WmMouseMove)
        {
            return 1;
        }

        return NativeMethods.CallNextHookEx(_mouseHookHandle, code, wParam, lParam);
    }

    private static TouchpadRegionBoundsConfiguration CloneBounds(
        TouchpadRegionBoundsConfiguration? source,
        TouchpadRegionBoundsConfiguration fallback)
    {
        return new TouchpadRegionBoundsConfiguration
        {
            Left = source?.Left ?? fallback.Left,
            Top = source?.Top ?? fallback.Top,
            Right = source?.Right ?? fallback.Right,
            Bottom = source?.Bottom ?? fallback.Bottom
        };
    }

    private static TouchpadDecodedContact? GetPrimaryContact(IReadOnlyList<TouchpadDecodedContact> contacts)
    {
        TouchpadDecodedContact? bestContact = null;
        foreach (var contact in contacts)
        {
            if (!contact.Tip && !contact.Confidence)
            {
                continue;
            }

            if (bestContact is null || contact.Pressure > bestContact.Value.Pressure)
            {
                bestContact = contact;
            }
        }

        return bestContact;
    }

    private string? ResolveRegionId(TouchpadDecodedContact? contact)
    {
        if (contact is null)
        {
            return null;
        }

        if (IsWithinCornerRegion(contact.Value.X, contact.Value.Y, TouchpadCornerRegionId.LeftTop, _leftTopBounds))
        {
            return TouchpadCornerRegionId.LeftTop;
        }

        if (IsWithinCornerRegion(contact.Value.X, contact.Value.Y, TouchpadCornerRegionId.RightTop, _rightTopBounds))
        {
            return TouchpadCornerRegionId.RightTop;
        }

        return null;
    }

    private bool IsWithinCornerRegion(int x, int y, string regionId, TouchpadRegionBoundsConfiguration bounds)
    {
        return TouchpadCornerRegionMath.ContainsPoint(regionId, bounds, x, y);
    }

    private static bool HasAssignedAction(ActionDefinitionConfiguration? action)
    {
        return !string.IsNullOrWhiteSpace(action?.Type);
    }

    private readonly record struct TouchpadContactMetrics(double CentroidX, double CentroidY, double Spread);

    private sealed class TouchpadTrackingContext
    {
        public bool HasInteraction { get; set; }

        public TouchpadGestureSessionOwner GestureSessionOwner { get; set; }

        public TouchpadGestureSessionPhase GestureSessionPhase { get; set; }

        public bool LastButtonPressed { get; set; }

        public int PressStartPressure { get; set; }

        public int PeakPressure { get; set; }

        public int LastPressure { get; set; }

        public bool DeepPressed { get; set; }

        public DateTimeOffset LongPressStartedAt { get; set; }

        public int DeepCandidateFrames { get; set; }

        public DateTimeOffset LastActiveAt { get; set; }

        public DateTimeOffset InteractionStartedAt { get; set; }

        public string? SessionRegionId { get; set; }

        public int StartX { get; set; }

        public int StartY { get; set; }

        public int FiveFingerPinchReadyFrames { get; set; }

        public int FiveFingerPinchInTriggerFrames { get; set; }

        public int FiveFingerPinchOutTriggerFrames { get; set; }

        public double FiveFingerPinchStartCentroidX { get; set; }

        public double FiveFingerPinchStartCentroidY { get; set; }

        public double FiveFingerPinchBaselineSpread { get; set; }

        public double FiveFingerPinchMaxObservedSpread { get; set; }

        public double FiveFingerPinchMinObservedSpread { get; set; }

        public bool EdgeSlideActive { get; set; }

        public TouchpadEdgeSlideSide? EdgeSlideSide { get; set; }

        public int EdgeSlideContactId { get; set; } = -1;

        public double EdgeSlideStartY { get; set; }

        public double EdgeSlideAnchorY { get; set; }

        public bool EdgeSlideTriggered { get; set; }
    }

    private static class NativeMethods
    {
        public const int WmInput = 0x00FF;
        public const int WmInputDeviceChange = 0x00FE;
        public const int RidInput = 0x10000003;
        public const int RidiDeviceName = 0x20000007;
        public const int RidiDeviceInfo = 0x2000000b;
        public const uint RimTypeHid = 2;
        public const int RidevInputSink = 0x00000100;
        public const int RidevDevNotify = 0x00002000;
        public static readonly nint WmMouseMove = new(0x0200);
        public const int WhMouseLl = 14;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] devices, uint deviceCount, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceInfo(nint deviceHandle, uint command, nint data, ref uint size);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetRawInputDeviceInfo(nint deviceHandle, uint command, StringBuilder data, ref uint size);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint SetWindowsHookEx(int hookId, LowLevelMouseProc callback, nint moduleHandle, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(nint hookHandle);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint CallNextHookEx(nint hookHandle, int code, nint wParam, nint lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint GetModuleHandle(string? moduleName);

        public delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public int dwFlags;
            public nint hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public nint hDevice;
            public nint wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWHIDHEADER
        {
            public uint dwSizeHid;
            public uint dwCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RID_DEVICE_INFO
        {
            public uint cbSize;
            public uint dwType;
            public RID_DEVICE_INFO_UNION Anonymous;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct RID_DEVICE_INFO_UNION
        {
            [FieldOffset(0)] public RID_DEVICE_INFO_HID hid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RID_DEVICE_INFO_HID
        {
            public uint dwVendorId;
            public uint dwProductId;
            public uint dwVersionNumber;
            public ushort usUsagePage;
            public ushort usUsage;
        }
    }
}

internal enum TouchpadGestureTriggerKind
{
    DeepPress,
    LongPress,
    FiveFingerPinchIn,
    FiveFingerPinchOut
}

internal enum TouchpadGestureSessionOwner
{
    None,
    Press,
    FiveFingerPinch
}

internal enum TouchpadGestureSessionPhase
{
    Idle,
    Tracking,
    Triggered
}

internal enum TouchpadEdgeSlideSide
{
    Left,
    Right
}

internal enum TouchpadEdgeSlideDirection
{
    Up,
    Down
}

internal sealed class TouchpadGestureTriggerEventArgs(
    TouchpadGestureTriggerKind triggerKind,
    string? regionId,
    int startX,
    int startY,
    int pressure) : EventArgs
{
    public TouchpadGestureTriggerKind TriggerKind { get; } = triggerKind;

    public string? RegionId { get; } = regionId;

    public int StartX { get; } = startX;

    public int StartY { get; } = startY;

    public int Pressure { get; } = pressure;
}

internal sealed class TouchpadEdgeSlideEventArgs(
    TouchpadEdgeSlideSide side,
    TouchpadEdgeSlideDirection direction,
    int steps,
    int x,
    int y,
    int pressure) : EventArgs
{
    public TouchpadEdgeSlideSide Side { get; } = side;

    public TouchpadEdgeSlideDirection Direction { get; } = direction;

    public int Steps { get; } = steps;

    public int X { get; } = x;

    public int Y { get; } = y;

    public int Pressure { get; } = pressure;
}
