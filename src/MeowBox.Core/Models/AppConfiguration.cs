using System.Text.Json.Serialization;
using MeowBox.Core.Services;

namespace MeowBox.Core.Models;

public sealed class AppConfiguration
{
    public string Theme { get; set; } = ThemePreference.System;

    public AppPreferences Preferences { get; set; } = new();

    public TouchpadConfiguration Touchpad { get; set; } = new();

    public List<KeyDefinitionConfiguration> Keys { get; set; } = new();

    public List<KeyActionMappingConfiguration> Mappings { get; set; } = new();

    public static AppConfiguration CreateDefault()
    {
        return SupportedDeviceConfiguration.CreateDefault();
    }
}

public sealed class AppPreferences
{
    public bool IsListening { get; set; } = true;

    public bool PreferPriorityStartup { get; set; } = true;

    public int SwitchToBatteryModeOnDcThresholdPercent { get; set; } = BatteryControlCatalog.AutoSwitchNeverThreshold;

    public string PreferredPerformanceModeKey { get; set; } = BatteryControlCatalog.DefaultPerformanceModeKey;

    public List<string> PerformanceModeCycleKeys { get; set; } = [.. BatteryControlCatalog.DefaultPerformanceModeCycleOrder];

    public bool ResetChargeLimitToFullOnStartup { get; set; }

    public int PreferredChargeLimitPercent { get; set; } = BatteryControlCatalog.DefaultChargeLimitPercent;

    public string Language { get; set; } = AppLanguagePreference.System;

    public bool ShowTrayIcon { get; set; } = true;

    public bool ShowEasterEggs { get; set; } = true;

    public string EasterEggActivationCode { get; set; } = string.Empty;

    public OsdPreferences Osd { get; set; } = new();
}

public sealed class OsdPreferences
{
    public string DisplayMode { get; set; } = OsdDisplayMode.IconOnly;

    public int DurationMs { get; set; } = RuntimeDefaults.DefaultOsdDurationMs;

    public int BackgroundOpacityPercent { get; set; } = RuntimeDefaults.DefaultOsdBackgroundOpacityPercent;

    public int ScalePercent { get; set; } = RuntimeDefaults.DefaultOsdScalePercent;
}

public sealed class KeyDefinitionConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public EventMatcherConfiguration Trigger { get; set; } = new();
}

public sealed class ActionDefinitionConfiguration
{
    public string Type { get; set; } = HotkeyActionType.None;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KeyChordConfiguration? KeyChord { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }
}

public sealed class KeyChordConfiguration
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrimaryKey { get; set; }

    public List<string> Modifiers { get; set; } = [];
}

public sealed class KeyActionMappingConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string KeyId { get; set; } = string.Empty;

    public ActionDefinitionConfiguration Action { get; set; } = new();
}

public sealed class EventMatcherConfiguration
{
    public string Source { get; set; } = InputSourceKind.Wmi;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WmiClassName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WmiActive { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReportHex { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? VirtualKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MakeCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Flags { get; set; }

    public bool IsMatch(InputEvent inputEvent)
    {
        if (!string.IsNullOrWhiteSpace(Source) &&
            !string.Equals(Source, inputEvent.Source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(WmiClassName) &&
            !string.Equals(WmiClassName, inputEvent.WmiClassName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (WmiActive.HasValue && WmiActive != inputEvent.WmiActive)
        {
            return false;
        }

        if (VirtualKey.HasValue && VirtualKey != inputEvent.VirtualKey)
        {
            return false;
        }

        if (MakeCode.HasValue && MakeCode != inputEvent.MakeCode)
        {
            return false;
        }

        if (Flags.HasValue && Flags != inputEvent.Flags)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ReportHex))
        {
            var expectedReport = NormalizeHex(ReportHex);
            var actualReport = NormalizeHex(inputEvent.ReportHex);
            var matchesReport = actualReport.Length >= expectedReport.Length
                ? actualReport.StartsWith(expectedReport, StringComparison.OrdinalIgnoreCase)
                : string.Equals(expectedReport, actualReport, StringComparison.OrdinalIgnoreCase);

            if (!matchesReport)
            {
                return false;
            }
        }

        return true;
    }

    public string ToDisplayText()
    {
        return System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    public static EventMatcherConfiguration FromInputEvent(InputEvent inputEvent)
    {
        return new EventMatcherConfiguration
        {
            Source = inputEvent.Source,
            WmiClassName = inputEvent.WmiClassName,
            WmiActive = inputEvent.WmiActive,
            ReportHex = inputEvent.ReportHex,
            VirtualKey = inputEvent.VirtualKey,
            MakeCode = inputEvent.MakeCode,
            Flags = inputEvent.Flags
        };
    }

    private static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var index = 0;
        foreach (var character in value.ToUpperInvariant())
        {
            if ((character >= '0' && character <= '9') ||
                (character >= 'A' && character <= 'F'))
            {
                buffer[index++] = character;
            }
        }

        return new string(buffer, 0, index);
    }
}

public sealed class IconConfiguration
{
    public string Mode { get; set; } = IconSourceMode.None;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }
}

public static class ThemePreference
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";
}

public static class AppLanguagePreference
{
    public const string System = "System";
    public const string English = "English";
    public const string Chinese = "Chinese";
}

public static class InputSourceKind
{
    public const string Wmi = "Wmi";
    public const string Keyboard = "Keyboard";
}

public static class HotkeyActionType
{
    public const string None = "";
    public const string CyclePerformanceMode = "CyclePerformanceMode";
    public const string ShowFnLockOsd = "ShowFnLockOsd";
    public const string ShowCapsLockOsd = "ShowCapsLockOsd";
    public const string ShowKeyboardBacklightOsd = "ShowKeyboardBacklightOsd";
    public const string SendStandardKey = "SendStandardKey";
    public const string OpenSettings = "OpenSettings";
    public const string OpenProjection = "OpenProjection";
    public const string ToggleTouchpad = "ToggleTouchpad";
    public const string MicrophoneMuteOn = "MicrophoneMuteOn";
    public const string MicrophoneMuteOff = "MicrophoneMuteOff";
    public const string VolumeUp = "VolumeUp";
    public const string VolumeDown = "VolumeDown";
    public const string VolumeMute = "VolumeMute";
    public const string MediaPrevious = "MediaPrevious";
    public const string MediaNext = "MediaNext";
    public const string MediaPlayPause = "MediaPlayPause";
    public const string BrightnessUp = "BrightnessUp";
    public const string BrightnessDown = "BrightnessDown";
    public const string ToggleAirplaneMode = "ToggleAirplaneMode";
    public const string LockWindows = "LockWindows";
    public const string Screenshot = "Screenshot";
    public const string OpenCalculator = "OpenCalculator";
    public const string OpenApplication = "OpenApplication";
}

public static class RuntimeDefaults
{
    public const int DefaultOsdDurationMs = 800;
    public const int DefaultOsdBackgroundOpacityPercent = 20;
    public const int DefaultOsdScalePercent = 75;
    public const int DefaultTouchpadLightPressThreshold = 125;
    public const int DefaultTouchpadDeepPressThreshold = 500;
    public const int DefaultTouchpadSurfaceWidth = 3282;
    public const int DefaultTouchpadSurfaceHeight = 2124;
    public const int DefaultTouchpadCornerWidth = 400;
    public const int DefaultTouchpadCornerHeight = 400;
    public const int DefaultTouchpadCornerLongPressDurationMs = 750;
    public const int MaxOsdTitleLength = 32;
}

public static class OsdDisplayMode
{
    public const string IconAndText = "IconAndText";
    public const string IconOnly = "IconOnly";
    public const string TextOnly = "TextOnly";
}

public static class IconSourceMode
{
    public const string None = "None";
    public const string CustomFile = "CustomFile";
}

public static class BuiltInOsdAsset
{
    public const string FnLock = "fn-lock";
    public const string FnUnlock = "fn-unlock";
    public const string CapsLock = "caps-lock";
    public const string CapsUnlock = "caps-unlock";
    public const string MicrophoneMute = "microphone-mute";
    public const string MicrophoneOn = "microphone-on";
    public const string BacklightOff = "backlight-off";
    public const string BacklightLow = "backlight-low";
    public const string BacklightHigh = "backlight-high";
    public const string BacklightAuto = "backlight-auto";
    public const string TouchpadOff = "touchpad-off";
    public const string TouchpadOn = "touchpad-on";
    public const string PerformanceBattery = "performance-battery";
    public const string PerformanceSilent = "performance-silent";
    public const string PerformanceSmart = "performance-smart";
    public const string PerformanceTurbo = "performance-turbo";
    public const string PerformanceBeast = "performance-beast";
}

public static class DefaultKeyIds
{
    public const string PerformanceModePress = "key-performance-mode-press";
    public const string FnLockToggle = "key-fn-lock-toggle";
    public const string CapsLockToggle = "key-caps-lock-toggle";
    public const string MicrophoneMuteOn = "key-mic-mute-on";
    public const string MicrophoneMuteOff = "key-mic-mute-off";
    public const string XiaoAiPress = "key-xiaoai-press";
    public const string XiaoAiRelease = "key-xiaoai-release";
    public const string SettingsPress = "key-settings-press";
    public const string ManagerPress = "key-manager-press";
    public const string ManagerRelease = "key-manager-release";
    public const string BacklightCycle = "key-backlight-cycle";
    public const string Projection = "key-projection";
}
