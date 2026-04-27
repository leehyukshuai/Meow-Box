using MeowBox.Core.Services;

namespace MeowBox.Core.Models;

public sealed class BatteryControlState
{
    public bool Supported { get; set; }

    public string InstanceName { get; set; } = string.Empty;

    public string PerformanceModeKey { get; set; } = BatteryControlCatalog.DefaultPerformanceModeKey;

    public int ChargeLimitPercent { get; set; } = BatteryControlCatalog.DefaultChargeLimitPercent;
}

public static class BatteryControlCatalog
{
    public const string Battery = "battery";
    public const string Silent = "silent";
    public const string Smart = "smart";
    public const string Turbo = "turbo";
    public const string Beast = "beast";

    public const string DefaultPerformanceModeKey = Smart;
    public const int DefaultChargeLimitPercent = 100;

    public static IReadOnlyList<string> PerformanceModeOrder { get; } =
    [
        Battery,
        Silent,
        Smart,
        Turbo,
        Beast
    ];

    public static IReadOnlyList<string> PerformanceModeCycleOrder { get; } =
    [
        Silent,
        Smart,
        Beast
    ];

    public static IReadOnlyList<int> ChargeLimitOrder { get; } =
    [
        40,
        50,
        60,
        70,
        80,
        90,
        100
    ];

    public static string GetPerformanceModeLabel(string modeKey)
    {
        return modeKey.ToLowerInvariant() switch
        {
            Battery => ResourceStringService.GetString("Osd.Title.PerformanceBatterySaver", "Battery saver"),
            Silent => ResourceStringService.GetString("Osd.Title.PerformanceSilent", "Silent"),
            Smart => ResourceStringService.GetString("Osd.Title.PerformanceSmart", "Smart"),
            Turbo => ResourceStringService.GetString("Osd.Title.PerformanceTurbo", "Turbo"),
            Beast => ResourceStringService.GetString("Osd.Title.PerformanceBeast", "Beast"),
            _ => ResourceStringService.GetString("Osd.Title.PerformanceUnknown", "Unknown")
        };
    }

    public static string GetNextCyclePerformanceModeKey(string? currentModeKey)
    {
        var normalizedModeKey = NormalizePerformanceModeKey(currentModeKey);
        var index = PerformanceModeCycleOrder
            .Select((key, position) => new { key, position })
            .FirstOrDefault(item => string.Equals(item.key, normalizedModeKey, StringComparison.OrdinalIgnoreCase))
            ?.position ?? -1;

        return index < 0
            ? Silent
            : PerformanceModeCycleOrder[(index + 1) % PerformanceModeCycleOrder.Count];
    }

    public static string? GetPerformanceModeOsdAssetKey(string? modeKey)
    {
        return NormalizePerformanceModeKey(modeKey) switch
        {
            Silent => BuiltInOsdAsset.PerformanceSilent,
            Smart => BuiltInOsdAsset.PerformanceSmart,
            Beast => BuiltInOsdAsset.PerformanceBeast,
            _ => null
        };
    }

    public static string GetChargeLimitLabel(int percent)
    {
        return percent >= 100
            ? ResourceStringService.GetString("Battery.ChargeLimit.Off", "100% (off)")
            : percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    public static ushort GetPerformanceRawCode(string modeKey)
    {
        return modeKey.ToLowerInvariant() switch
        {
            Battery => 0x000A,
            Silent => 0x0002,
            Smart => 0x0009,
            Turbo => 0x0003,
            Beast => 0x0004,
            _ => throw new ArgumentOutOfRangeException(nameof(modeKey), modeKey, "Unknown performance mode.")
        };
    }

    public static string GetPerformanceModeKey(ushort rawCode)
    {
        return rawCode switch
        {
            0x000A => Battery,
            0x0002 => Silent,
            0x0009 => Smart,
            0x0003 => Turbo,
            0x0004 => Beast,
            _ => throw new InvalidOperationException("Unknown performance mode code: 0x" + rawCode.ToString("X4", System.Globalization.CultureInfo.InvariantCulture))
        };
    }

    public static uint GetChargeLimitRawCode(int percent)
    {
        return percent switch
        {
            100 => 0,
            80 => 1,
            90 => 4,
            70 => 5,
            60 => 6,
            50 => 7,
            40 => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(percent), percent, "Unsupported charge limit percent.")
        };
    }

    public static int GetChargeLimitPercent(uint rawCode)
    {
        return rawCode switch
        {
            0 => 100,
            1 => 80,
            4 => 90,
            5 => 70,
            6 => 60,
            7 => 50,
            8 => 40,
            _ => throw new InvalidOperationException("Unknown charge limit code: " + rawCode.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
    }

    public static int NormalizeChargeLimitPercent(int percent)
    {
        return ChargeLimitOrder.Contains(percent)
            ? percent
            : DefaultChargeLimitPercent;
    }

    public static string NormalizePerformanceModeKey(string? modeKey)
    {
        if (string.IsNullOrWhiteSpace(modeKey))
        {
            return DefaultPerformanceModeKey;
        }

        return PerformanceModeOrder.FirstOrDefault(item => string.Equals(item, modeKey, StringComparison.OrdinalIgnoreCase))
            ?? DefaultPerformanceModeKey;
    }
}
