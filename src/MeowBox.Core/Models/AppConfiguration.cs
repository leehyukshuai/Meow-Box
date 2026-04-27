using System.Globalization;
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

    public bool ResetPerformanceModeToSmartOnStartup { get; set; } = true;

    public string PreferredPerformanceModeKey { get; set; } = BatteryControlCatalog.DefaultPerformanceModeKey;

    public bool ResetChargeLimitToFullOnStartup { get; set; }

    public int PreferredChargeLimitPercent { get; set; } = BatteryControlCatalog.DefaultChargeLimitPercent;

    public string Language { get; set; } = AppLanguagePreference.System;

    public bool ShowTrayIcon { get; set; } = true;

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

public sealed class MappingOsdConfiguration
{
    public bool Enabled { get; set; }
}

public sealed class KeyActionMappingConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string KeyId { get; set; } = string.Empty;

    public ActionDefinitionConfiguration Action { get; set; } = new();

    public MappingOsdConfiguration Osd { get; set; } = new();
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
    public const string PerformanceSilent = "performance-silent";
    public const string PerformanceSmart = "performance-smart";
    public const string PerformanceBeast = "performance-beast";
}

public static class ActionTag
{
    public const string All = "all";
    public const string System = "system";
    public const string Keyboard = "keyboard";
    public const string Display = "display";
    public const string Audio = "audio";
    public const string Media = "media";
    public const string Application = "application";
}

public sealed record ChoiceOption(string Key, string Label);

public sealed record ActionTagOption(string Key, string Label);

public sealed class ActionOption
{
    public ActionOption(string key, string label, string description, string iconGlyph, params string[] tags)
    {
        Key = key;
        Label = label;
        Description = description;
        IconGlyph = iconGlyph;
        Tags = tags.Length == 0 ? [ActionTag.System] : tags;
    }

    public string Key { get; }

    public string Label { get; }

    public string Description { get; }

    public string IconGlyph { get; }

    public IReadOnlyList<string> Tags { get; }

    public string TagsText => string.Join(" · ", Tags.Select(ActionCatalog.GetTagLabel));
}

public sealed class IconAssetOption
{
    public IconAssetOption(string key, string label, string iconGlyph)
    {
        Key = key;
        Label = label;
        IconGlyph = iconGlyph;
    }

    public string Key { get; }

    public string Label { get; }

    public string IconGlyph { get; }
}

public static class ActionCatalog
{
    public static string NoActionLabel => ResourceStringService.GetString("Action.NoSelection", "No action selected");
    public static string NoActionDescription => ResourceStringService.GetString("Action.NoSelectionDescription", "No action is assigned to this mapping.");
    public const string NoActionIconGlyph = "";

    public static IReadOnlyList<ActionTagOption> TagOptions { get; } =
    [
        new(ActionTag.All, ResourceStringService.GetString("ActionTag.All", "All")),
        new(ActionTag.System, ResourceStringService.GetString("ActionTag.System", "System")),
        new(ActionTag.Keyboard, ResourceStringService.GetString("ActionTag.Keyboard", "Keyboard")),
        new(ActionTag.Display, ResourceStringService.GetString("ActionTag.Display", "Display")),
        new(ActionTag.Audio, ResourceStringService.GetString("ActionTag.Audio", "Audio")),
        new(ActionTag.Media, ResourceStringService.GetString("ActionTag.Media", "Media")),
        new(ActionTag.Application, ResourceStringService.GetString("ActionTag.Application", "Application"))
    ];

    public static IReadOnlyList<ActionOption> All { get; } = new[]
    {
        new ActionOption(HotkeyActionType.SendStandardKey, ResourceStringService.GetString("Action.SendKey.Label", "Send key or shortcut"), ResourceStringService.GetString("Action.SendKey.Description", "Sends the keyboard key or modifier shortcut that you configure."), "", ActionTag.Keyboard, ActionTag.System),
        new ActionOption(HotkeyActionType.OpenSettings, ResourceStringService.GetString("Action.OpenSettings.Label", "Open Windows Settings"), ResourceStringService.GetString("Action.OpenSettings.Description", "Launches the native Settings app."), "", ActionTag.System),
        new ActionOption(HotkeyActionType.OpenProjection, ResourceStringService.GetString("Action.OpenProjection.Label", "Open projection switcher"), ResourceStringService.GetString("Action.OpenProjection.Description", "Launches the native projection overlay."), "", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.ToggleTouchpad, ResourceStringService.GetString("Action.ToggleTouchpad.Label", "Toggle touchpad"), ResourceStringService.GetString("Action.ToggleTouchpad.Description", "Turns the Windows touchpad off or back on on supported devices."), "\uEFA5", ActionTag.System),
        new ActionOption(HotkeyActionType.MicrophoneMuteOn, ResourceStringService.GetString("Action.MicMuteOn.Label", "Mute microphone input"), ResourceStringService.GetString("Action.MicMuteOn.Description", "Turns the default microphone capture device off."), "", ActionTag.System, ActionTag.Audio),
        new ActionOption(HotkeyActionType.MicrophoneMuteOff, ResourceStringService.GetString("Action.MicMuteOff.Label", "Unmute microphone input"), ResourceStringService.GetString("Action.MicMuteOff.Description", "Turns the default microphone capture device back on."), "", ActionTag.System, ActionTag.Audio),
        new ActionOption(HotkeyActionType.VolumeUp, ResourceStringService.GetString("Action.VolumeUp.Label", "Volume up"), ResourceStringService.GetString("Action.VolumeUp.Description", "Raises the master output volume."), "", ActionTag.Audio, ActionTag.Media),
        new ActionOption(HotkeyActionType.VolumeDown, ResourceStringService.GetString("Action.VolumeDown.Label", "Volume down"), ResourceStringService.GetString("Action.VolumeDown.Description", "Lowers the master output volume."), "", ActionTag.Audio, ActionTag.Media),
        new ActionOption(HotkeyActionType.VolumeMute, ResourceStringService.GetString("Action.VolumeMute.Label", "Toggle volume mute"), ResourceStringService.GetString("Action.VolumeMute.Description", "Toggles the system speaker mute state."), "", ActionTag.Audio, ActionTag.Media),
        new ActionOption(HotkeyActionType.MediaPrevious, ResourceStringService.GetString("Action.MediaPrevious.Label", "Previous track"), ResourceStringService.GetString("Action.MediaPrevious.Description", "Sends the previous-track media key."), "", ActionTag.Media),
        new ActionOption(HotkeyActionType.MediaNext, ResourceStringService.GetString("Action.MediaNext.Label", "Next track"), ResourceStringService.GetString("Action.MediaNext.Description", "Sends the next-track media key."), "", ActionTag.Media),
        new ActionOption(HotkeyActionType.MediaPlayPause, ResourceStringService.GetString("Action.MediaPlayPause.Label", "Play or pause"), ResourceStringService.GetString("Action.MediaPlayPause.Description", "Sends the media play-pause key."), "", ActionTag.Media),
        new ActionOption(HotkeyActionType.BrightnessUp, ResourceStringService.GetString("Action.BrightnessUp.Label", "Brightness up"), ResourceStringService.GetString("Action.BrightnessUp.Description", "Raises the internal display brightness."), "", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.BrightnessDown, ResourceStringService.GetString("Action.BrightnessDown.Label", "Brightness down"), ResourceStringService.GetString("Action.BrightnessDown.Description", "Lowers the internal display brightness."), "", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.ToggleAirplaneMode, ResourceStringService.GetString("Action.ToggleAirplaneMode.Label", "Toggle airplane mode"), ResourceStringService.GetString("Action.ToggleAirplaneMode.Description", "Turns supported radios off or back on."), "", ActionTag.System),
        new ActionOption(HotkeyActionType.LockWindows, ResourceStringService.GetString("Action.LockWindows.Label", "Lock Windows"), ResourceStringService.GetString("Action.LockWindows.Description", "Locks the current Windows session."), "", ActionTag.System),
        new ActionOption(HotkeyActionType.Screenshot, ResourceStringService.GetString("Action.Screenshot.Label", "Take screenshot"), ResourceStringService.GetString("Action.Screenshot.Description", "Opens the native snipping overlay."), "", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.OpenCalculator, ResourceStringService.GetString("Action.OpenCalculator.Label", "Open Calculator"), ResourceStringService.GetString("Action.OpenCalculator.Description", "Launches Calculator."), "", ActionTag.System, ActionTag.Application),
        new ActionOption(HotkeyActionType.OpenApplication, ResourceStringService.GetString("Action.OpenApplication.Label", "Open application"), ResourceStringService.GetString("Action.OpenApplication.Description", "Launches an installed app, shortcut, or executable."), "", ActionTag.Application),
        new ActionOption(HotkeyActionType.CyclePerformanceMode, ResourceStringService.GetString("Action.CyclePerformanceMode.Label", "Toggle performance mode"), ResourceStringService.GetString("Action.CyclePerformanceMode.Description", "Cycles between Silent, Smart, and Beast modes."), "", ActionTag.System)
    };

    public static bool IsKnownActionType(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return string.Equals(key, HotkeyActionType.CyclePerformanceMode, StringComparison.OrdinalIgnoreCase) ||
               All.Any(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetLabel(string key)
    {
        if (string.Equals(key, HotkeyActionType.CyclePerformanceMode, StringComparison.OrdinalIgnoreCase))
        {
            return ResourceStringService.GetString("Action.CyclePerformanceMode.Label", "Toggle performance mode");
        }

        return GetOption(key)?.Label ?? NoActionLabel;
    }

    public static string GetDescription(string key)
    {
        if (string.Equals(key, HotkeyActionType.CyclePerformanceMode, StringComparison.OrdinalIgnoreCase))
        {
            return ResourceStringService.GetString("Action.CyclePerformanceMode.Description", "Cycles between Silent, Smart, and Beast modes.");
        }

        return GetOption(key)?.Description ?? NoActionDescription;
    }

    public static string GetIconGlyph(string key)
    {
        if (string.Equals(key, HotkeyActionType.CyclePerformanceMode, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return GetOption(key)?.IconGlyph ?? NoActionIconGlyph;
    }

    public static IReadOnlyList<string> GetTags(string key)
    {
        if (string.Equals(key, HotkeyActionType.CyclePerformanceMode, StringComparison.OrdinalIgnoreCase))
        {
            return [ActionTag.System];
        }

        return GetOption(key)?.Tags ?? [];
    }

    public static string GetTagsText(string key)
    {
        return string.Join(" · ", GetTags(key).Select(GetTagLabel));
    }

    public static ActionOption? GetOption(string key)
    {
        return All.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    public static bool MatchesTag(ActionOption option, string? tag)
    {
        return string.IsNullOrWhiteSpace(tag) ||
               string.Equals(tag, ActionTag.All, StringComparison.OrdinalIgnoreCase) ||
               option.Tags.Any(item => string.Equals(item, tag, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetTagLabel(string key)
    {
        return TagOptions.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))?.Label ?? ResourceStringService.GetString("ActionTag.Other", "Other");
    }
}

public static class MappingDisplayCatalog
{
    private const string OsdIconGlyph = "";

    public static string ShowOsdLabel => ResourceStringService.GetString("Mapping.ShowOsd", "Show OSD");

    public static string BuildListActionLabel(string actionType, bool osdEnabled)
    {
        var hasAction = !string.IsNullOrWhiteSpace(actionType);
        if (hasAction && osdEnabled)
        {
            return string.Format(CultureInfo.CurrentCulture, "{0} + {1}", ActionCatalog.GetLabel(actionType), ShowOsdLabel);
        }

        if (hasAction)
        {
            return ActionCatalog.GetLabel(actionType);
        }

        return osdEnabled ? ShowOsdLabel : ActionCatalog.NoActionLabel;
    }

    public static string GetIconGlyph(string actionType, bool osdEnabled)
    {
        if (!string.IsNullOrWhiteSpace(actionType))
        {
            return ActionCatalog.GetIconGlyph(actionType);
        }

        return osdEnabled ? OsdIconGlyph : ActionCatalog.NoActionIconGlyph;
    }
}

public static class IconAssetCatalog
{
    public static IReadOnlyList<ChoiceOption> OsdDisplayModes { get; } =
    [
        new(OsdDisplayMode.IconAndText, ResourceStringService.GetString("OsdDisplayMode.IconAndTitle", "Icon + title")),
        new(OsdDisplayMode.IconOnly, ResourceStringService.GetString("OsdDisplayMode.IconOnly", "Icon only")),
        new(OsdDisplayMode.TextOnly, ResourceStringService.GetString("OsdDisplayMode.TextOnly", "Title only"))
    ];
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
