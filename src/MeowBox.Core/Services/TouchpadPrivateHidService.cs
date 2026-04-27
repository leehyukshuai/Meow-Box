using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MeowBox.Core.Services;

public static class TouchpadPrivateHidService
{
    private const string PathMarker = "hid#bltp7853&col05";
    private const int ReportLength = 33;
    private const int InterPacketDelayMs = 130;
    private const int PulseInitializeDelayMs = 10;
    private const int PulseRepeatDelayMs = 18;
    private static readonly object DevicePathSync = new();
    private static string? _cachedDevicePath;

    public static string GetDevicePath()
    {
        NativeMethods.HidD_GetHidGuid(out var hidGuid);
        var infoSet = NativeMethods.SetupDiGetClassDevs(ref hidGuid, null, nint.Zero, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (infoSet == NativeMethods.InvalidHandleValue)
        {
            throw new InvalidOperationException("SetupDiGetClassDevs failed.");
        }

        try
        {
            var index = 0;
            while (true)
            {
                var interfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>() };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(infoSet, nint.Zero, ref hidGuid, index, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    throw new InvalidOperationException($"SetupDiEnumDeviceInterfaces failed: {error}");
                }

                NativeMethods.SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, nint.Zero, 0, out var requiredSize, nint.Zero);
                var detailBuffer = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detailBuffer, requiredSize, out _, nint.Zero))
                    {
                        throw new InvalidOperationException($"SetupDiGetDeviceInterfaceDetail failed: {Marshal.GetLastWin32Error()}");
                    }

                    var path = Marshal.PtrToStringUni(detailBuffer + 4);
                    if (path is not null && path.Contains(PathMarker, StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }

                index++;
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(infoSet);
        }

        throw new InvalidOperationException("Touchpad HID collection not found.");
    }

    public static byte[] SetHaptic(bool enabled)
        => SendSequence(enabled ? PacketCatalog.HapticOn : PacketCatalog.HapticOff);

    public static byte[] Pulse()
    {
        using var session = CreatePulseSession();
        session.Pulse();
        return Array.Empty<byte>();
    }

    public static PulseSession CreatePulseSession()
    {
        var handle = OpenDeviceHandle();
        try
        {
            WritePacket(handle, PacketCatalog.Initialize, PulseInitializeDelayMs);
            return new PulseSession(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public static void PulseBurst(int repeatCount)
    {
        var safeRepeatCount = Math.Max(1, repeatCount);
        using var session = CreatePulseSession();
        for (var index = 0; index < safeRepeatCount; index++)
        {
            session.Pulse(index == safeRepeatCount - 1 ? 0 : PulseRepeatDelayMs);
        }
    }

    public static byte[] SetVibration(int mode)
        => SendSequence(PacketCatalog.GetVibration(mode));

    public static byte[] SetPress(int mode)
        => SendSequence(PacketCatalog.GetPress(mode));

    public static string ToHex(ReadOnlySpan<byte> bytes)
        => BitConverter.ToString(bytes.ToArray()).Replace('-', ' ');

    public sealed class PulseSession : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private bool _disposed;

        internal PulseSession(SafeFileHandle handle)
        {
            _handle = handle;
        }

        public void Pulse(int delayMs = 0)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            WritePacket(_handle, PacketCatalog.Pulse[0], delayMs);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _handle.Dispose();
        }
    }

    private static byte[] SendSequence(
        IReadOnlyList<byte[]> packets,
        int interPacketDelayMs = InterPacketDelayMs,
        bool readInputReport = true)
    {
        using var handle = OpenDeviceHandle();
        WritePacket(handle, PacketCatalog.Initialize, interPacketDelayMs);

        foreach (var packet in packets)
        {
            WritePacket(handle, packet, interPacketDelayMs);
        }

        if (!readInputReport)
        {
            return Array.Empty<byte>();
        }

        var input = new byte[ReportLength];
        input[0] = 0x0D;
        if (!NativeMethods.HidD_GetInputReport(handle, input, input.Length))
        {
            throw new InvalidOperationException($"HidD_GetInputReport failed: {Marshal.GetLastWin32Error()}");
        }

        return input;
    }

    private static SafeFileHandle OpenDeviceHandle()
    {
        var path = GetCachedDevicePath();
        var handle = TryOpenDeviceHandle(path);
        if (!handle.IsInvalid)
        {
            return handle;
        }

        handle.Dispose();
        InvalidateCachedDevicePath();
        path = GetCachedDevicePath();
        handle = TryOpenDeviceHandle(path);
        if (handle.IsInvalid)
        {
            throw new InvalidOperationException($"CreateFile failed: {Marshal.GetLastWin32Error()}");
        }

        return handle;
    }

    private static SafeFileHandle TryOpenDeviceHandle(string path)
    {
        return NativeMethods.CreateFile(
            path,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            nint.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            nint.Zero);
    }

    private static string GetCachedDevicePath()
    {
        lock (DevicePathSync)
        {
            _cachedDevicePath ??= GetDevicePath();
            return _cachedDevicePath;
        }
    }

    private static void InvalidateCachedDevicePath()
    {
        lock (DevicePathSync)
        {
            _cachedDevicePath = null;
        }
    }

    private static void WritePacket(SafeFileHandle handle, byte[] packet, int delayMs)
    {
        if (!NativeMethods.HidD_SetOutputReport(handle, packet, packet.Length))
        {
            throw new InvalidOperationException($"HidD_SetOutputReport failed: {Marshal.GetLastWin32Error()} | {ToHex(packet)}");
        }

        if (delayMs > 0)
        {
            Thread.Sleep(delayMs);
        }
    }

    private static class PacketCatalog
    {
        public static readonly byte[] Initialize = Build(0x0D, 0x07, 0x16, 0x00, 0x00, 0x01, 0x00, 0x04, 0x10);

        public static readonly IReadOnlyList<byte[]> HapticOn =
        [
            Build(0x0D, 0x09, 0x5C, 0x00, 0x00, 0x03, 0x00, 0x00, 0x59, 0x01),
            Build(0x0D, 0x09, 0xFE, 0x01, 0x00, 0x03, 0x00, 0x00, 0x01, 0x59, 0xA7),
            Build(0x0D, 0x07, 0x5C, 0x01, 0x00, 0x01, 0x00, 0x02, 0x59)
        ];

        public static readonly IReadOnlyList<byte[]> HapticOff =
        [
            Build(0x0D, 0x09, 0x59, 0x00, 0x00, 0x03, 0x00, 0x00, 0x59, 0x02),
            Build(0x0D, 0x09, 0xFE, 0x01, 0x00, 0x03, 0x00, 0x00, 0x01, 0x59, 0xA7),
            Build(0x0D, 0x07, 0x5C, 0x01, 0x00, 0x01, 0x00, 0x02, 0x59)
        ];

        public static readonly IReadOnlyList<byte[]> Pulse =
        [
            Build(0x0D, 0x09, 0xFD, 0x01, 0x00, 0x03, 0x00, 0x00, 0x02, 0x32, 0xCE, 0x00)
        ];

        public static IReadOnlyList<byte[]> GetVibration(int mode) => mode switch
        {
            1 =>
            [
                Build(0x0D, 0x0B, 0x31, 0x00, 0x00, 0x05, 0x00, 0x00, 0x5D, 0x38, 0x00, 0x50, 0x00),
                Build(0x0D, 0x09, 0xFE, 0x01, 0x00, 0x03, 0x00, 0x00, 0x01, 0x5D, 0xA3),
                Build(0x0D, 0x07, 0x5A, 0x01, 0x00, 0x01, 0x00, 0x04, 0x5D)
            ],
            2 =>
            [
                Build(0x0D, 0x0B, 0x61, 0x00, 0x00, 0x05, 0x00, 0x00, 0x5D, 0x50, 0x00, 0x68, 0x00),
                Build(0x0D, 0x09, 0xFE, 0x01, 0x00, 0x03, 0x00, 0x00, 0x01, 0x5D, 0xA3),
                Build(0x0D, 0x07, 0x5A, 0x01, 0x00, 0x01, 0x00, 0x04, 0x5D)
            ],
            3 =>
            [
                Build(0x0D, 0x0B, 0xB1, 0x00, 0x00, 0x05, 0x00, 0x00, 0x5D, 0x68, 0x00, 0x80, 0x00),
                Build(0x0D, 0x09, 0xFE, 0x01, 0x00, 0x03, 0x00, 0x00, 0x01, 0x5D, 0xA3),
                Build(0x0D, 0x07, 0x5A, 0x01, 0x00, 0x01, 0x00, 0x04, 0x5D)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        public static IReadOnlyList<byte[]> GetPress(int mode) => mode switch
        {
            1 =>
            [
                Build(0x0D, 0x0F, 0x92, 0x00, 0x00, 0x09, 0x00, 0x00, 0x5B, 0x96, 0x00, 0x31, 0x00, 0xF4, 0x01, 0x90, 0x01),
                Build(0x0D, 0x09, 0xFE, 0x01, 0x00, 0x03, 0x00, 0x00, 0x01, 0x5B, 0xA5),
                Build(0x0D, 0x07, 0x54, 0x01, 0x00, 0x01, 0x00, 0x08, 0x5B)
            ],
            2 =>
            [
                Build(0x0D, 0x0F, 0x63, 0x00, 0x00, 0x09, 0x00, 0x00, 0x5B, 0x7D, 0x00, 0x29, 0x00, 0xF4, 0x01, 0x90, 0x01),
                Build(0x0D, 0x09, 0xFE, 0x01, 0x00, 0x03, 0x00, 0x00, 0x01, 0x5B, 0xA5),
                Build(0x0D, 0x07, 0x54, 0x01, 0x00, 0x01, 0x00, 0x08, 0x5B)
            ],
            3 =>
            [
                Build(0x0D, 0x0F, 0x7E, 0x00, 0x00, 0x09, 0x00, 0x00, 0x5B, 0x69, 0x00, 0x22, 0x00, 0xF4, 0x01, 0x90, 0x01),
                Build(0x0D, 0x09, 0xFE, 0x01, 0x00, 0x03, 0x00, 0x00, 0x01, 0x5B, 0xA5),
                Build(0x0D, 0x07, 0x54, 0x01, 0x00, 0x01, 0x00, 0x08, 0x5B)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        private static byte[] Build(params byte[] prefix)
        {
            var bytes = new byte[ReportLength];
            Array.Copy(prefix, bytes, prefix.Length);
            return bytes;
        }
    }

    private static class NativeMethods
    {
        public const int DIGCF_PRESENT = 0x2;
        public const int DIGCF_DEVICEINTERFACE = 0x10;
        public const int ERROR_NO_MORE_ITEMS = 259;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x1;
        public const uint FILE_SHARE_WRITE = 0x2;
        public const uint OPEN_EXISTING = 3;
        public static readonly nint InvalidHandleValue = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct GUID
        {
            public uint Data1;
            public ushort Data2;
            public ushort Data3;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Data4;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public GUID InterfaceClassGuid;
            public int Flags;
            public nuint Reserved;
        }

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out GUID hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_SetOutputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetInputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint SetupDiGetClassDevs(ref GUID classGuid, string? enumerator, nint hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, ref GUID interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(nint deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, nint deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, nint deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, int flagsAndAttributes, nint templateFile);
    }
}
