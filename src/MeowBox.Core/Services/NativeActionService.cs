using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using MeowBox.Core.Models;

namespace MeowBox.Core.Services;

public sealed class NativeActionService
{
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
        SendVirtualKey(VkVolumeUp);
    }

    public void VolumeDown()
    {
        SendVirtualKey(VkVolumeDown);
    }

    public void VolumeMute()
    {
        SendVirtualKey(VkVolumeMute);
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
        StepBrightness(+10);
    }

    public void BrightnessDown()
    {
        StepBrightness(-10);
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

    private static void SendVirtualKey(ushort key)
    {
        SendKeyChord(key, []);
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

    private const ushort VkLeftWindows = 0x5B;
    private const ushort VkLetterI = 0x49;
    private const ushort VkVolumeMute = 0xAD;
    private const ushort VkVolumeDown = 0xAE;
    private const ushort VkVolumeUp = 0xAF;
    private const ushort VkMediaNext = 0xB0;
    private const ushort VkMediaPrevious = 0xB1;
    private const ushort VkMediaPlayPause = 0xB3;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, KeyEventF dwFlags, nuint dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [Flags]
    private enum KeyEventF : uint
    {
        KeyUp = 0x0002
    }
}
