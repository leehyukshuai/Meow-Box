using System.Runtime.InteropServices;

namespace MeowBox.Core.Services;

public sealed class WindowsPowerModeService
{
    public bool IsAcPowered()
    {
        try
        {
            return GetSystemPowerStatus(out var status) && status.ACLineStatus == 1;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
