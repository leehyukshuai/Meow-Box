using System.Runtime.InteropServices;

namespace MeowBox.Core.Services;

public sealed class WindowsPowerModeService
{
    public const string HighPerformanceSchemeAlias = "SCHEME_MIN";
    public const string BalancedSchemeAlias = "SCHEME_BALANCED";
    public const string PowerSaverSchemeAlias = "SCHEME_MAX";

    private static readonly Guid HighPerformanceSchemeGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid BalancedSchemeGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid PowerSaverSchemeGuid = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    private static readonly Guid EnergySaverSubgroupGuid = new("de830923-a562-41af-a086-e3a2c6bad2da");
    private static readonly Guid EnergySaverBatteryThresholdSettingGuid = new("e69653ca-cf7f-4f05-aa73-cb833fa90ad4");

    public bool IsAcPowered()
    {
        try
        {
            return TryGetSystemPowerStatus(out var status) && status.ACLineStatus == 1;
        }
        catch
        {
            return false;
        }
    }

    public int GetBatteryLevelPercent()
    {
        try
        {
            if (!TryGetSystemPowerStatus(out var status) || status.BatteryLifePercent == byte.MaxValue)
            {
                return -1;
            }

            return Math.Clamp((int)status.BatteryLifePercent, 0, 100);
        }
        catch
        {
            return -1;
        }
    }

    public void SetActiveScheme(string schemeAlias)
    {
        if (string.IsNullOrWhiteSpace(schemeAlias))
        {
            throw new ArgumentException("A power scheme alias is required.", nameof(schemeAlias));
        }

        var schemeGuid = ResolveSchemeGuid(schemeAlias);
        var result = PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
        if (result == 0)
        {
            return;
        }

        throw new InvalidOperationException("Failed to set the active Windows power scheme. Win32 error: " + result.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
    }

    public void SetEnergySaverBatteryThresholdPercent(int percent)
    {
        var normalizedPercent = NormalizeEnergySaverBatteryThresholdPercent(percent);
        var activeSchemeGuid = GetActiveSchemeGuid();
        var schemeGuids = new List<Guid>
        {
            HighPerformanceSchemeGuid,
            BalancedSchemeGuid,
            PowerSaverSchemeGuid
        };

        if (!schemeGuids.Contains(activeSchemeGuid))
        {
            schemeGuids.Add(activeSchemeGuid);
        }

        foreach (var schemeGuid in schemeGuids)
        {
            WriteEnergySaverBatteryThresholdPercent(schemeGuid, normalizedPercent);
        }

        ApplyCurrentSchemeSettings(activeSchemeGuid);
    }

    private static Guid ResolveSchemeGuid(string schemeAlias)
    {
        return schemeAlias.ToUpperInvariant() switch
        {
            HighPerformanceSchemeAlias => HighPerformanceSchemeGuid,
            BalancedSchemeAlias => BalancedSchemeGuid,
            PowerSaverSchemeAlias => PowerSaverSchemeGuid,
            _ => throw new ArgumentOutOfRangeException(nameof(schemeAlias), schemeAlias, "Unknown Windows power scheme alias.")
        };
    }

    private static int NormalizeEnergySaverBatteryThresholdPercent(int percent)
    {
        if (percent <= 0)
        {
            return percent == -1 ? 100 : 0;
        }

        return Math.Clamp(percent, 0, 100);
    }

    private static void WriteEnergySaverBatteryThresholdPercent(Guid schemeGuid, int percent)
    {
        var subgroupGuid = EnergySaverSubgroupGuid;
        var powerSettingGuid = EnergySaverBatteryThresholdSettingGuid;
        var result = PowerWriteDCValueIndex(
            IntPtr.Zero,
            ref schemeGuid,
            ref subgroupGuid,
            ref powerSettingGuid,
            (uint)percent);
        ThrowIfPowerApiFailed(result, "write the Windows Energy Saver battery threshold");
    }

    private static Guid GetActiveSchemeGuid()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var activeSchemeGuidPointer);
        ThrowIfPowerApiFailed(result, "read the active Windows power scheme");

        if (activeSchemeGuidPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not read the active Windows power scheme.");
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(activeSchemeGuidPointer);
        }
        finally
        {
            _ = LocalFree(activeSchemeGuidPointer);
        }
    }

    private static void ApplyCurrentSchemeSettings(Guid activeSchemeGuid)
    {
        var result = PowerSetActiveScheme(IntPtr.Zero, ref activeSchemeGuid);
        ThrowIfPowerApiFailed(result, "apply the active Windows power scheme settings");
    }

    private static void ThrowIfPowerApiFailed(uint result, string action)
    {
        if (result == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Could not " + action + ". Win32 error: " +
            result.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
    }

    private static bool TryGetSystemPowerStatus(out SystemPowerStatus status)
    {
        return GetSystemPowerStatus(out status);
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerWriteDCValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupGuid,
        ref Guid powerSettingGuid,
        uint dcValueIndex);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

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
