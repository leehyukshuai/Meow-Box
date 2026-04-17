using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Worker.Services;

internal sealed class TouchpadInputService : NativeWindow, IDisposable
{
    private static readonly TimeSpan StaleInteractionTimeout = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan SessionResetThreshold = TimeSpan.FromMilliseconds(350);
    private const int LongPressPressureThreshold = 120;
    private const int GestureRearmPressureThreshold = 100;

    private readonly object _sync = new();
    private readonly Dictionary<nint, RawInputDeviceSnapshot> _deviceCache = [];
    private static readonly RawInputDeviceSnapshot FallbackTouchpadDevice = new(0, 0x0D, 0x05, "Precision touchpad", string.Empty, true);
    private readonly System.Threading.Timer _staleStateTimer;

    private TouchpadTrackingContext _context = new();
    private TouchpadLiveStateSnapshot _latestState = new()
    {
        DeepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold
    };

    private int _deepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold;
    private int _longPressDurationMs = RuntimeDefaults.DefaultTouchpadCornerLongPressDurationMs;
    private TouchpadRegionBoundsConfiguration _leftTopBounds = TouchpadCornerRegionConfiguration.CreateLeftTopDefault().Bounds;
    private TouchpadRegionBoundsConfiguration _rightTopBounds = TouchpadCornerRegionConfiguration.CreateRightTopDefault().Bounds;
    private DateTimeOffset _lastFrameAt;
    private bool _disposed;

    public TouchpadInputService()
    {
        CreateHandle(new CreateParams
        {
            Caption = "FnMappingTool.TouchpadInput",
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
    public event EventHandler<TouchpadLiveStateSnapshot>? StateChanged;

    public void UpdateConfiguration(TouchpadConfiguration? configuration)
    {
        configuration ??= new TouchpadConfiguration();

        lock (_sync)
        {
            _deepPressThreshold = Math.Clamp(
                configuration.DeepPressThreshold,
                100,
                4000);
            _longPressDurationMs = Math.Clamp(
                configuration.LongPressDurationMs,
                200,
                3000);
            _leftTopBounds = CloneBounds(configuration.LeftTopCorner?.Bounds, TouchpadCornerRegionConfiguration.CreateLeftTopDefault().Bounds);
            _rightTopBounds = CloneBounds(configuration.RightTopCorner?.Bounds, TouchpadCornerRegionConfiguration.CreateRightTopDefault().Bounds);
            _latestState.DeepPressThreshold = _deepPressThreshold;
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
        var contacts = parsed.Contacts
            .Where(static contact => contact.Tip || contact.Confidence)
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
        lock (_sync)
        {
            var primaryContact = GetPrimaryContact(parsed.Contacts);
            var currentRegionId = ResolveRegionId(primaryContact);
            var threshold = _deepPressThreshold;
            if (hasInteraction && (!_context.HasInteraction || timestamp - _context.LastActiveAt > SessionResetThreshold))
            {
                _context = CreateTrackingContext(timestamp);
            }

            var shouldRearm = !hasInteraction || !parsed.Button1 || pressure <= GestureRearmPressureThreshold;
            if (shouldRearm)
            {
                ResetGestureCycle(timestamp, pressure);
            }
            else
            {
                if (!_context.GestureCycleActive)
                {
                    StartGestureCycle(primaryContact, currentRegionId, pressure, timestamp);
                }
                else if (!string.IsNullOrWhiteSpace(currentRegionId) &&
                         string.IsNullOrWhiteSpace(_context.SessionRegionId) &&
                         !_context.GestureConsumed)
                {
                    _context.SessionRegionId = currentRegionId;
                    _context.StartX = primaryContact?.X ?? _context.StartX;
                    _context.StartY = primaryContact?.Y ?? _context.StartY;
                }

                var pressureDelta = pressure - _context.LastPressure;
                var pressStartPressure = _context.PressStartPressure > 0 ? _context.PressStartPressure : pressure;
                var aboveDeepThreshold = parsed.Button1 &&
                                         pressure >= threshold &&
                                         pressure - pressStartPressure >= 20;
                var deepCandidateFrames = aboveDeepThreshold ? _context.DeepCandidateFrames + 1 : 0;
                if (!_context.GestureConsumed &&
                    parsed.Button1 &&
                    !_context.DeepPressed &&
                    deepCandidateFrames >= 2)
                {
                    _context.DeepPressed = true;
                    _context.GestureConsumed = true;
                    _context.LongPressStartedAt = default;
                    gestureTriggered = new TouchpadGestureTriggerEventArgs(
                        TouchpadGestureTriggerKind.DeepPress,
                        _context.SessionRegionId,
                        _context.StartX,
                        _context.StartY,
                        pressure);
                }

                var releaseFloor = Math.Max(GestureRearmPressureThreshold, threshold - 40);
                if (_context.DeepPressed &&
                    (!parsed.Button1 || !hasInteraction || pressure <= releaseFloor || pressureDelta <= -25))
                {
                    _context.DeepPressed = false;
                    deepCandidateFrames = 0;
                }

                if (!_context.GestureConsumed &&
                    !string.IsNullOrWhiteSpace(_context.SessionRegionId))
                {
                    if (_context.LongPressStartedAt == default &&
                        pressure >= LongPressPressureThreshold)
                    {
                        _context.LongPressStartedAt = timestamp;
                    }

                    if (_context.LongPressStartedAt != default &&
                        !_context.LongPressTriggered &&
                        timestamp - _context.LongPressStartedAt >= TimeSpan.FromMilliseconds(_longPressDurationMs))
                    {
                        _context.LongPressTriggered = true;
                        _context.GestureConsumed = true;
                        gestureTriggered ??= new TouchpadGestureTriggerEventArgs(
                            TouchpadGestureTriggerKind.LongPress,
                            _context.SessionRegionId,
                            _context.StartX,
                            _context.StartY,
                            pressure);
                    }
                }

                _context.DeepCandidateFrames = deepCandidateFrames;
            }

            _context.LastPressure = pressure;
            _context.LastButtonPressed = parsed.Button1;
            _context.HasInteraction = hasInteraction;
            if (hasInteraction)
            {
                _context.LastActiveAt = timestamp;
                _context.PeakPressure = Math.Max(_context.PeakPressure, pressure);
            }

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

    private void StartGestureCycle(
        TouchpadDecodedContact? primaryContact,
        string? currentRegionId,
        int pressure,
        DateTimeOffset timestamp)
    {
        _context.GestureCycleActive = true;
        _context.PressStartPressure = pressure;
        _context.PeakPressure = pressure;
        _context.InteractionStartedAt = timestamp;
        _context.SessionRegionId = currentRegionId;
        _context.StartX = primaryContact?.X ?? 0;
        _context.StartY = primaryContact?.Y ?? 0;
        _context.DeepPressed = false;
        _context.GestureConsumed = false;
        _context.LongPressTriggered = false;
        _context.LongPressStartedAt = default;
        _context.DeepCandidateFrames = 0;
    }

    private void ResetGestureCycle(DateTimeOffset timestamp, int pressure)
    {
        _context.GestureCycleActive = false;
        _context.PressStartPressure = 0;
        _context.PeakPressure = pressure;
        _context.DeepPressed = false;
        _context.GestureConsumed = false;
        _context.LongPressTriggered = false;
        _context.LongPressStartedAt = default;
        _context.DeepCandidateFrames = 0;
        _context.SessionRegionId = null;
        _context.StartX = 0;
        _context.StartY = 0;
        _context.InteractionStartedAt = timestamp;
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

    private sealed class TouchpadTrackingContext
    {
        public bool HasInteraction { get; set; }

        public bool GestureCycleActive { get; set; }

        public bool LastButtonPressed { get; set; }

        public int PressStartPressure { get; set; }

        public int PeakPressure { get; set; }

        public int LastPressure { get; set; }

        public bool DeepPressed { get; set; }

        public bool LongPressTriggered { get; set; }

        public bool GestureConsumed { get; set; }

        public DateTimeOffset LongPressStartedAt { get; set; }

        public int DeepCandidateFrames { get; set; }

        public DateTimeOffset LastActiveAt { get; set; }

        public DateTimeOffset InteractionStartedAt { get; set; }

        public string? SessionRegionId { get; set; }

        public int StartX { get; set; }

        public int StartY { get; set; }
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

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] devices, uint deviceCount, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceInfo(nint deviceHandle, uint command, nint data, ref uint size);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetRawInputDeviceInfo(nint deviceHandle, uint command, StringBuilder data, ref uint size);

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
    LongPress
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
