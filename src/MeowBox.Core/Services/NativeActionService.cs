using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MeowBox.Core.Models;

namespace MeowBox.Core.Services;

public sealed class NativeActionService
{
    private static readonly object BrightnessDeviceSync = new();

    public void OpenSettings()
    {
        SendKeyChord(VkLetterI, [VkLeftWindows]);
    }

    public void OpenProjection()
    {
        Launch(@"C:\Windows\System32\DisplaySwitch.exe");
    }

    public void OpenCalculator()
    {
        Launch("calc.exe");
    }

    public void ToggleTouchpad()
    {
        SendKeyChord(VkF24, [VkLeftControl, VkLeftWindows]);
    }

    public void SendConfiguredKeyChord(KeyChordConfiguration? keyChord)
    {
        var normalizedChord = StandardKeyCatalog.NormalizeChord(keyChord);
        if (normalizedChord is null ||
            !StandardKeyCatalog.TryGetVirtualKey(normalizedChord.PrimaryKey, out var primaryKey))
        {
            return;
        }

        var modifiers = new List<ushort>();
        foreach (var modifierKey in normalizedChord.Modifiers)
        {
            if (StandardKeyCatalog.TryGetModifierVirtualKey(modifierKey, out var modifierVirtualKey))
            {
                modifiers.Add(modifierVirtualKey);
            }
        }

        SendKeyChord(primaryKey, modifiers);
    }

    public void VolumeUp()
    {
        SendRepeatedVirtualKey(VkVolumeUp, 5);
    }

    public void VolumeDown()
    {
        SendRepeatedVirtualKey(VkVolumeDown, 5);
    }

    public void VolumeMute()
    {
        SendVirtualKey(VkVolumeMute);
    }

    public void EdgeSlideVolumeUp()
    {
        SendVirtualKey(VkVolumeUp);
    }

    public void EdgeSlideVolumeDown()
    {
        SendVirtualKey(VkVolumeDown);
    }

    public void MediaPrevious()
    {
        SendVirtualKey(VkMediaPrevious);
    }

    public void MediaNext()
    {
        SendVirtualKey(VkMediaNext);
    }

    public void MediaPlayPause()
    {
        SendVirtualKey(VkMediaPlayPause);
    }

    public void LockWindows()
    {
        LockWorkStation();
    }

    public void Screenshot()
    {
        Launch("ms-screenclip:");
    }

    public void BrightnessUp()
    {
        StepBrightness(+1);
    }

    public void BrightnessDown()
    {
        StepBrightness(-1);
    }

    public void BrightnessEdgeSlideUp()
    {
        StepBrightnessWmi(+2);
    }

    public void BrightnessEdgeSlideDown()
    {
        StepBrightnessWmi(-2);
    }

    public void ReleaseBrightnessAdjustment()
    {
        TryReleaseBrightnessDevice();
    }

    public async Task ToggleAirplaneModeAsync()
    {
        var access = await Windows.Devices.Radios.Radio.RequestAccessAsync();
        if (access != Windows.Devices.Radios.RadioAccessStatus.Allowed)
        {
            return;
        }

        var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
        var toggleable = radios.Where(radio => radio.Kind is Windows.Devices.Radios.RadioKind.WiFi or Windows.Devices.Radios.RadioKind.MobileBroadband or Windows.Devices.Radios.RadioKind.Bluetooth).ToList();
        if (toggleable.Count == 0)
        {
            return;
        }

        var shouldDisable = toggleable.Any(radio => radio.State == Windows.Devices.Radios.RadioState.On);
        foreach (var radio in toggleable)
        {
            try
            {
                await radio.SetStateAsync(shouldDisable ? Windows.Devices.Radios.RadioState.Off : Windows.Devices.Radios.RadioState.On);
            }
            catch
            {
            }
        }
    }

    public void LaunchConfiguredTarget(string target, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var normalizedTarget = target.Trim();
        if (normalizedTarget.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = normalizedTarget,
                UseShellExecute = true
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = normalizedTarget,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,
            WorkingDirectory = ResolveWorkingDirectory(normalizedTarget)
        });
    }

    private static void Launch(string target)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    private static string ResolveWorkingDirectory(string target)
    {
        try
        {
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }
        catch
        {
        }

        return AppContext.BaseDirectory;
    }

    private static void StepBrightness(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        StepBrightnessWmi(delta > 0 ? +5 : -5);
    }

    private static void StepBrightnessWmi(int delta)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness");
            var current = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (current is null)
            {
                return;
            }

            var brightness = Convert.ToInt32((byte)current["CurrentBrightness"]);
            var target = Math.Clamp(brightness + delta, 0, 100);

            using var methods = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (var method in methods.Get().Cast<ManagementObject>())
            {
                method.InvokeMethod("WmiSetBrightness", new object[] { 0u, (byte)target });
            }
        }
        catch
        {
        }
    }

    private static bool TryStepBrightnessDevice(bool up)
    {
        try
        {
            lock (BrightnessDeviceSync)
            {
                using var handle = OpenBrightnessDevice();
                var pressPacket = up ? BrightnessUpPacket : BrightnessDownPacket;
                if (!WriteBrightnessPacket(handle, pressPacket))
                {
                    return false;
                }

                Thread.Sleep(BrightnessPressHoldMs);
                return WriteBrightnessPacket(handle, BrightnessReleasePacket) &&
                       WriteBrightnessPacket(handle, BrightnessReleasePacket);
            }
        }
        catch
        {
            try
            {
                TryReleaseBrightnessDevice();
            }
            catch
            {
            }

            return false;
        }
    }

    private static bool TryReleaseBrightnessDevice()
    {
        try
        {
            lock (BrightnessDeviceSync)
            {
                using var handle = OpenBrightnessDevice();
                return WriteBrightnessPacket(handle, BrightnessReleasePacket) &&
                       WriteBrightnessPacket(handle, BrightnessReleasePacket) &&
                       WriteBrightnessPacket(handle, BrightnessReleasePacket);
            }
        }
        catch
        {
            return false;
        }
    }

    private static SafeFileHandle OpenBrightnessDevice()
    {
        var handle = CreateFile(
            BrightnessDevicePath,
            GenericWrite,
            FileShareRead | FileShareWrite,
            nint.Zero,
            OpenExisting,
            0,
            nint.Zero);

        if (handle.IsInvalid)
        {
            throw new InvalidOperationException("Failed to open brightness control device.");
        }

        return handle;
    }

    private static bool WriteBrightnessPacket(SafeFileHandle handle, byte[] packet)
    {
        return WriteFile(handle, packet, packet.Length, out var written, nint.Zero) && written == packet.Length;
    }

    private static void SendVirtualKey(ushort key)
    {
        SendKeyChord(key, []);
    }

    private static void SendRepeatedVirtualKey(ushort key, int repeatCount)
    {
        for (var index = 0; index < Math.Max(1, repeatCount); index++)
        {
            SendVirtualKey(key);
        }
    }

    private static void SendKeyChord(ushort primaryKey, IReadOnlyList<ushort> modifiers)
    {
        foreach (var modifier in modifiers)
        {
            keybd_event((byte)modifier, 0, 0, 0);
        }

        keybd_event((byte)primaryKey, 0, 0, 0);
        keybd_event((byte)primaryKey, 0, KeyEventF.KeyUp, 0);

        for (var index = modifiers.Count - 1; index >= 0; index--)
        {
            keybd_event((byte)modifiers[index], 0, KeyEventF.KeyUp, 0);
        }
    }

    private const ushort VkLeftControl = 0xA2;
    private const ushort VkLeftWindows = 0x5B;
    private const ushort VkLetterI = 0x49;
    private const ushort VkF24 = 0x87;
    private const ushort VkVolumeMute = 0xAD;
    private const ushort VkVolumeDown = 0xAE;
    private const ushort VkVolumeUp = 0xAF;
    private const ushort VkMediaNext = 0xB0;
    private const ushort VkMediaPrevious = 0xB1;
    private const ushort VkMediaPlayPause = 0xB3;
    private const string BrightnessDevicePath = @"\\?\ROOT#SYSTEM#0001#{8888f630-72b2-11d2-b852-00c04fad5171}";
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;
    private const int BrightnessPressHoldMs = 12;
    private static readonly byte[] BrightnessUpPacket = [0x01, 0x70, 0x00];
    private static readonly byte[] BrightnessDownPacket = [0x01, 0x6F, 0x00];
    private static readonly byte[] BrightnessReleasePacket = [0x00, 0x00, 0x00];

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, KeyEventF dwFlags, nuint dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, int flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, int numberOfBytesToWrite, out int numberOfBytesWritten, nint overlapped);

    [Flags]
    private enum KeyEventF : uint
    {
        KeyUp = 0x0002
    }
}
