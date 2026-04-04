using System.Text.Json.Serialization;
using FnMappingTool.Core.Services;

namespace FnMappingTool.Core.Models;

public sealed class AppConfiguration
{
    public string Theme { get; set; } = ThemePreference.System;

    public AppPreferences Preferences { get; set; } = new();

    public List<KeyDefinitionConfiguration> Keys { get; set; } = new();

    public List<KeyActionMappingConfiguration> Mappings { get; set; } = new();

    public static AppConfiguration CreateDefault()
    {
        return new AppConfiguration
        {
            Preferences = new AppPreferences
            {
                IsListening = true
            }
        };
    }
}

public sealed class AppPreferences
{
    public bool IsListening { get; set; } = true;

    public bool ShowTrayIcon { get; set; } = true;

    public OsdPreferences Osd { get; set; } = new();
}

public sealed class OsdPreferences
{
    public string DisplayMode { get; set; } = OsdDisplayMode.IconAndText;

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
    public string? Target { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OsdTitle { get; set; }

    public IconConfiguration OsdIcon { get; set; } = new();
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

        if (!string.IsNullOrWhiteSpace(ReportHex) &&
            !string.Equals(NormalizeHex(ReportHex), NormalizeHex(inputEvent.ReportHex), StringComparison.OrdinalIgnoreCase))
        {
            return false;
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

public static class InputSourceKind
{
    public const string Wmi = "Wmi";
    public const string Keyboard = "Keyboard";
}

public static class HotkeyActionType
{
    public const string None = "";
    public const string OpenSettings = "OpenSettings";
    public const string OpenProjection = "OpenProjection";
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
    public const string ShowOsd = "ShowOsd";
    public const string OpenApplication = "OpenApplication";
}

public static class RuntimeDefaults
{
    public const int DefaultOsdDurationMs = 800;
    public const int DefaultOsdBackgroundOpacityPercent = 20;
    public const int DefaultOsdScalePercent = 100;
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
    public const string BacklightOff = "backlight-off";
    public const string BacklightLevel1 = "backlight-level1";
    public const string BacklightLevel2 = "backlight-level2";
    public const string BacklightAuto = "backlight-auto";
}

public static class ActionTag
{
    public const string All = "all";
    public const string System = "system";
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
    public const string NoActionLabel = "Select action";
    public const string NoActionDescription = "No action is assigned to this mapping.";
    public const string NoActionIconGlyph = "\uE711";

    public static IReadOnlyList<ActionTagOption> TagOptions { get; } =
    [
        new(ActionTag.All, "All"),
        new(ActionTag.System, "System"),
        new(ActionTag.Display, "Display"),
        new(ActionTag.Audio, "Audio"),
        new(ActionTag.Media, "Media"),
        new(ActionTag.Application, "Application")
    ];

    public static IReadOnlyList<ActionOption> All { get; } = new[]
    {
        new ActionOption(HotkeyActionType.OpenSettings, "Open Windows Settings", "Launches the native Settings app.", "\uE713", ActionTag.System),
        new ActionOption(HotkeyActionType.OpenProjection, "Open projection switcher", "Launches the native projection overlay.", "\uE7F4", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.MicrophoneMuteOn, "Mute microphone input", "Turns the default microphone capture device off.", "\uE720", ActionTag.System, ActionTag.Audio),
        new ActionOption(HotkeyActionType.MicrophoneMuteOff, "Unmute microphone input", "Turns the default microphone capture device back on.", "\uE720", ActionTag.System, ActionTag.Audio),
        new ActionOption(HotkeyActionType.VolumeUp, "Volume up", "Raises the master output volume.", "\uE767", ActionTag.Audio, ActionTag.Media),
        new ActionOption(HotkeyActionType.VolumeDown, "Volume down", "Lowers the master output volume.", "\uE768", ActionTag.Audio, ActionTag.Media),
        new ActionOption(HotkeyActionType.VolumeMute, "Toggle volume mute", "Toggles the system speaker mute state.", "\uE74F", ActionTag.Audio, ActionTag.Media),
        new ActionOption(HotkeyActionType.MediaPrevious, "Previous track", "Sends the previous-track media key.", "\uE892", ActionTag.Media),
        new ActionOption(HotkeyActionType.MediaNext, "Next track", "Sends the next-track media key.", "\uE893", ActionTag.Media),
        new ActionOption(HotkeyActionType.MediaPlayPause, "Play or pause", "Sends the media play-pause key.", "\uE768", ActionTag.Media),
        new ActionOption(HotkeyActionType.BrightnessUp, "Brightness up", "Raises the internal display brightness.", "\uE706", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.BrightnessDown, "Brightness down", "Lowers the internal display brightness.", "\uE706", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.ToggleAirplaneMode, "Toggle airplane mode", "Turns supported radios off or back on.", "\uE709", ActionTag.System),
        new ActionOption(HotkeyActionType.LockWindows, "Lock Windows", "Locks the current Windows session.", "\uE72E", ActionTag.System),
        new ActionOption(HotkeyActionType.Screenshot, "Take screenshot", "Opens the native snipping overlay.", "\uE722", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.OpenCalculator, "Open Calculator", "Launches Calculator.", "\uE8EF", ActionTag.System, ActionTag.Application),
        new ActionOption(HotkeyActionType.ShowOsd, "Show OSD", "Displays a centered lower translucent OSD card.", "\uE7F4", ActionTag.Display),
        new ActionOption(HotkeyActionType.OpenApplication, "Open application", "Launches an installed app, shortcut, or executable.", "\uE71D", ActionTag.Application)
    };

    public static string GetLabel(string key)
    {
        return GetOption(key)?.Label ?? NoActionLabel;
    }

    public static string GetDescription(string key)
    {
        return GetOption(key)?.Description ?? NoActionDescription;
    }

    public static string GetIconGlyph(string key)
    {
        return GetOption(key)?.IconGlyph ?? NoActionIconGlyph;
    }

    public static IReadOnlyList<string> GetTags(string key)
    {
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
        return TagOptions.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))?.Label ?? "Other";
    }
}

public static class IconAssetCatalog
{
    public static IReadOnlyList<ChoiceOption> OsdDisplayModes { get; } =
    [
        new(OsdDisplayMode.IconAndText, "Icon + title"),
        new(OsdDisplayMode.IconOnly, "Icon only"),
        new(OsdDisplayMode.TextOnly, "Title only")
    ];
}

public static class DefaultKeyIds
{
    public const string FnLockOn = "key-fn-lock-on";
    public const string FnLockOff = "key-fn-lock-off";
    public const string MicrophoneMuteOn = "key-mic-mute-on";
    public const string MicrophoneMuteOff = "key-mic-mute-off";
    public const string XiaoAiPress = "key-xiaoai-press";
    public const string XiaoAiRelease = "key-xiaoai-release";
    public const string SettingsPress = "key-settings-press";
    public const string ManagerPress = "key-manager-press";
    public const string ManagerRelease = "key-manager-release";
    public const string BacklightOff = "key-backlight-off";
    public const string BacklightLevel1 = "key-backlight-level1";
    public const string BacklightLevel2 = "key-backlight-level2";
    public const string BacklightAuto = "key-backlight-auto";
    public const string Projection = "key-projection";
}
