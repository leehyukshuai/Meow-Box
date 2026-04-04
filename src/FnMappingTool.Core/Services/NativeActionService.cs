using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace FnMappingTool.Core.Services;

public sealed class NativeActionService
{
    public void OpenSettings()
    {
        SendModifiedVirtualKey(VkLeftWindows, VkLetterI);
    }

    public void OpenProjection()
    {
        Launch(@"C:\Windows\System32\DisplaySwitch.exe");
    }

    public void OpenCalculator()
    {
        Launch("calc.exe");
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

    private static void SendVirtualKey(byte key)
    {
        keybd_event(key, 0, 0, 0);
        keybd_event(key, 0, 2, 0);
    }

    private static void SendModifiedVirtualKey(byte modifier, byte key)
    {
        keybd_event(modifier, 0, 0, 0);
        keybd_event(key, 0, 0, 0);
        keybd_event(key, 0, 2, 0);
        keybd_event(modifier, 0, 2, 0);
    }

    private const byte VkLeftWindows = 0x5B;
    private const byte VkLetterI = 0x49;
    private const byte VkVolumeMute = 0xAD;
    private const byte VkVolumeDown = 0xAE;
    private const byte VkVolumeUp = 0xAF;
    private const byte VkMediaNext = 0xB0;
    private const byte VkMediaPrevious = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();
}
