using MeowBox.Core.Services;

namespace MeowBox.Core.Models;

public sealed class BatteryControlState
{
    public bool Supported { get; set; }

    public string InstanceName { get; set; } = string.Empty;

    public string PerformanceModeKey { get; set; } = BatteryControlCatalog.DefaultPerformanceModeKey;

    public string SelectedPerformanceModeKey { get; set; } = BatteryControlCatalog.DefaultSelectedPerformanceModeKey;

    public bool IsBatterySaverEnabled { get; set; }

    public bool IsAcPowered { get; set; }

    public int BatteryLevelPercent { get; set; } = -1;

    public int ChargeLimitPercent { get; set; } = BatteryControlCatalog.DefaultChargeLimitPercent;
}

public static class BatteryControlCatalog
{
    public const string Battery = "battery";
    public const string Silent = "silent";
    public const string Smart = "smart";
    public const string Turbo = "turbo";
    public const string Beast = "beast";
    public const string Extreme = "extreme";
    public const int AutoSwitchNeverThreshold = 0;
    public const int AutoSwitchAlwaysThreshold = -1;

    public const string DefaultPerformanceModeKey = Smart;
    public const string DefaultSelectedPerformanceModeKey = Smart;
    public const int DefaultChargeLimitPercent = 100;

    public static IReadOnlyList<string> PerformanceModeOrder { get; } =
    [
        Battery,
        Silent,
        Smart,
        Turbo,
        Beast
    ];

    public static IReadOnlyList<string> DefaultPerformanceModeCycleOrder { get; } =
    [
        Smart,
        Silent,
        Extreme
    ];

    public static IReadOnlyList<string> PerformanceModeCycleOptionOrder { get; } =
    [
        Smart,
        Silent,
        Extreme
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

    public static IReadOnlyList<int> BatteryModeOnDcThresholdOrder { get; } =
    [
        AutoSwitchNeverThreshold,
        10,
        20,
        30,
        40,
        50,
        60,
        70,
        80,
        90,
        AutoSwitchAlwaysThreshold
    ];

    public static IReadOnlyList<int> ExtremeModeOnAcThresholdOrder { get; } =
    [
        AutoSwitchNeverThreshold,
        60,
        70,
        80,
        90,
        100,
        AutoSwitchAlwaysThreshold
    ];

    public static string GetPerformanceModeLabel(string modeKey)
    {
        return modeKey.ToLowerInvariant() switch
        {
            Battery => ResourceStringService.GetString("Osd.Title.PerformanceBatterySaver", "Battery saver"),
            Silent => ResourceStringService.GetString("Osd.Title.PerformanceSilent", "Silent"),
            Smart => ResourceStringService.GetString("Osd.Title.PerformanceSmart", "Smart"),
            Turbo or Beast or Extreme => ResourceStringService.GetString("Osd.Title.PerformanceExtreme", "Extreme"),
            _ => ResourceStringService.GetString("Osd.Title.PerformanceUnknown", "Unknown")
        };
    }

    public static string GetSelectedPerformanceModeLabel(string modeKey)
    {
        return NormalizeSelectedPerformanceModeKey(modeKey) switch
        {
            Battery => ResourceStringService.GetString("Battery.Performance.BatterySaver", "Battery saver"),
            Silent => ResourceStringService.GetString("Battery.Performance.Silent", "Silent"),
            Smart => ResourceStringService.GetString("Battery.Performance.Smart", "Smart"),
            Extreme => ResourceStringService.GetString("Battery.Performance.Extreme", "Extreme"),
            _ => ResourceStringService.GetString("Battery.Performance.Smart", "Smart")
        };
    }

    public static string GetPerformanceModeCycleLabel(string modeKey)
    {
        return GetSelectedPerformanceModeLabel(modeKey);
    }

    public static string GetNextCyclePerformanceModeKey(
        string? currentRawModeKey,
        string? currentSelectedModeKey,
        bool isAcPowered,
        IEnumerable<string>? cycleModeKeys)
    {
        var cycleOrder = NormalizePerformanceModeCycleKeys(cycleModeKeys);
        var normalizedCurrentCycleModeKey = NormalizePerformanceModeCycleKey(
            ResolveSelectedPerformanceModeKey(currentRawModeKey, currentSelectedModeKey));
        var currentIndex = cycleOrder.FindIndex(item =>
            string.Equals(item, normalizedCurrentCycleModeKey, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            return cycleOrder[0];
        }

        return cycleOrder[(currentIndex + 1) % cycleOrder.Count];
    }

    public static string? GetPerformanceModeOsdAssetKey(string? modeKey)
    {
        return NormalizePerformanceModeKey(modeKey) switch
        {
            Battery => BuiltInOsdAsset.PerformanceBattery,
            Silent => BuiltInOsdAsset.PerformanceSilent,
            Smart => BuiltInOsdAsset.PerformanceSmart,
            Turbo => BuiltInOsdAsset.PerformanceTurbo,
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

    public static string ResolveRawPerformanceModeKey(string? selectedModeKey, bool isAcPowered)
    {
        return NormalizeSelectedPerformanceModeKey(selectedModeKey) switch
        {
            Extreme => isAcPowered ? Beast : Turbo,
            _ => NormalizePerformanceModeKey(selectedModeKey)
        };
    }

    public static string ResolveSelectedPerformanceModeKey(string? rawModeKey, string? fallbackSelectedModeKey = null)
    {
        return string.IsNullOrWhiteSpace(rawModeKey)
            ? NormalizeSelectedPerformanceModeKey(fallbackSelectedModeKey)
            : NormalizePerformanceModeKey(rawModeKey) switch
            {
                Turbo or Beast => Extreme,
                var normalizedModeKey => normalizedModeKey
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

    public static int NormalizeBatteryModeOnDcThresholdPercent(int percent)
    {
        return BatteryModeOnDcThresholdOrder.Contains(percent)
            ? percent
            : AutoSwitchNeverThreshold;
    }

    public static int NormalizeExtremeModeOnAcThresholdPercent(int percent)
    {
        return ExtremeModeOnAcThresholdOrder.Contains(percent)
            ? percent
            : AutoSwitchNeverThreshold;
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

    public static string NormalizeSelectedPerformanceModeKey(string? modeKey)
    {
        if (string.IsNullOrWhiteSpace(modeKey))
        {
            return DefaultSelectedPerformanceModeKey;
        }

        if (string.Equals(modeKey, Extreme, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(modeKey, Turbo, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(modeKey, Beast, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(modeKey, "turbo-beast-auto", StringComparison.OrdinalIgnoreCase))
        {
            return Extreme;
        }

        return modeKey.ToLowerInvariant() switch
        {
            Battery => Battery,
            Silent => Silent,
            Smart => Smart,
            _ => DefaultSelectedPerformanceModeKey
        };
    }

    public static string NormalizePerformanceModeCycleKey(string? modeKey)
    {
        return NormalizeSelectedPerformanceModeKey(modeKey);
    }

    public static List<string> NormalizePerformanceModeCycleKeys(IEnumerable<string>? modeKeys)
    {
        var orderedKeys = new List<string>();
        foreach (var modeKey in modeKeys ?? [])
        {
            var normalizedModeKey = NormalizePerformanceModeCycleKey(modeKey);
            if (!PerformanceModeCycleOptionOrder.Any(item => string.Equals(item, normalizedModeKey, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (orderedKeys.Contains(normalizedModeKey, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            orderedKeys.Add(normalizedModeKey);
        }

        if (orderedKeys.Count == 0)
        {
            orderedKeys.AddRange(DefaultPerformanceModeCycleOrder);
        }

        return orderedKeys;
    }

    public static string GetDefaultPerformanceModeCycleKey(IEnumerable<string>? modeKeys)
    {
        return NormalizePerformanceModeCycleKeys(modeKeys)[0];
    }

    public static string ResolveCyclePerformanceModeKey(string? cycleModeKey, bool isAcPowered)
    {
        return NormalizePerformanceModeCycleKey(cycleModeKey) switch
        {
            Extreme => isAcPowered ? Beast : Turbo,
            _ => NormalizePerformanceModeKey(cycleModeKey)
        };
    }
}
