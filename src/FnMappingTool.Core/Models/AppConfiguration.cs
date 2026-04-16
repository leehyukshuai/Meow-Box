using System.Globalization;
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
        return SupportedDeviceConfiguration.CreateDefault();
    }
}

public sealed class AppPreferences
{
    public bool IsListening { get; set; } = true;

    public bool PreferPriorityStartup { get; set; } = false;

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
    public string? StandardKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OsdTitle { get; set; }

    public IconConfiguration OsdIcon { get; set; } = new();
}

public sealed class MappingOsdConfiguration
{
    public bool Enabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    public IconConfiguration Icon { get; set; } = new();
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
    public const string SendStandardKey = "SendStandardKey";
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
    public const int DefaultOsdScalePercent = 75;
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
    public static string NoActionLabel => LocalizedText.Pick("Select action", "选择动作");
    public static string NoActionDescription => LocalizedText.Pick("No action is assigned to this mapping.", "这个映射还没有分配动作。");
    public const string NoActionIconGlyph = "";

    public static IReadOnlyList<ActionTagOption> TagOptions { get; } =
    [
        new(ActionTag.All, LocalizedText.Pick("All", "全部")),
        new(ActionTag.System, LocalizedText.Pick("System", "系统")),
        new(ActionTag.Keyboard, LocalizedText.Pick("Keyboard", "键盘")),
        new(ActionTag.Display, LocalizedText.Pick("Display", "显示")),
        new(ActionTag.Audio, LocalizedText.Pick("Audio", "音频")),
        new(ActionTag.Media, LocalizedText.Pick("Media", "媒体")),
        new(ActionTag.Application, LocalizedText.Pick("Application", "应用"))
    ];

    public static IReadOnlyList<ActionOption> All { get; } = new[]
    {
        new ActionOption(HotkeyActionType.SendStandardKey, LocalizedText.Pick("Send standard key", "发送标准按键"), LocalizedText.Pick("Sends a standard keyboard or media key that you choose.", "发送你选择的标准键盘按键或媒体按键。"), "", ActionTag.Keyboard, ActionTag.System),
        new ActionOption(HotkeyActionType.OpenSettings, LocalizedText.Pick("Open Windows Settings", "打开 Windows 设置"), LocalizedText.Pick("Launches the native Settings app.", "启动系统设置应用。"), "", ActionTag.System),
        new ActionOption(HotkeyActionType.OpenProjection, LocalizedText.Pick("Open projection switcher", "打开投影切换器"), LocalizedText.Pick("Launches the native projection overlay.", "打开系统投影切换界面。"), "", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.MicrophoneMuteOn, LocalizedText.Pick("Mute microphone input", "麦克风静音"), LocalizedText.Pick("Turns the default microphone capture device off.", "关闭默认麦克风采集设备。"), "", ActionTag.System, ActionTag.Audio),
        new ActionOption(HotkeyActionType.MicrophoneMuteOff, LocalizedText.Pick("Unmute microphone input", "取消麦克风静音"), LocalizedText.Pick("Turns the default microphone capture device back on.", "重新打开默认麦克风采集设备。"), "", ActionTag.System, ActionTag.Audio),
        new ActionOption(HotkeyActionType.VolumeUp, LocalizedText.Pick("Volume up", "音量增加"), LocalizedText.Pick("Raises the master output volume.", "提高系统主音量。"), "", ActionTag.Audio, ActionTag.Media),
        new ActionOption(HotkeyActionType.VolumeDown, LocalizedText.Pick("Volume down", "音量减小"), LocalizedText.Pick("Lowers the master output volume.", "降低系统主音量。"), "", ActionTag.Audio, ActionTag.Media),
        new ActionOption(HotkeyActionType.VolumeMute, LocalizedText.Pick("Toggle volume mute", "切换静音"), LocalizedText.Pick("Toggles the system speaker mute state.", "切换系统扬声器静音状态。"), "", ActionTag.Audio, ActionTag.Media),
        new ActionOption(HotkeyActionType.MediaPrevious, LocalizedText.Pick("Previous track", "上一曲"), LocalizedText.Pick("Sends the previous-track media key.", "发送上一曲媒体按键。"), "", ActionTag.Media),
        new ActionOption(HotkeyActionType.MediaNext, LocalizedText.Pick("Next track", "下一曲"), LocalizedText.Pick("Sends the next-track media key.", "发送下一曲媒体按键。"), "", ActionTag.Media),
        new ActionOption(HotkeyActionType.MediaPlayPause, LocalizedText.Pick("Play or pause", "播放或暂停"), LocalizedText.Pick("Sends the media play-pause key.", "发送媒体播放/暂停按键。"), "", ActionTag.Media),
        new ActionOption(HotkeyActionType.BrightnessUp, LocalizedText.Pick("Brightness up", "亮度增加"), LocalizedText.Pick("Raises the internal display brightness.", "提高内置显示器亮度。"), "", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.BrightnessDown, LocalizedText.Pick("Brightness down", "亮度减小"), LocalizedText.Pick("Lowers the internal display brightness.", "降低内置显示器亮度。"), "", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.ToggleAirplaneMode, LocalizedText.Pick("Toggle airplane mode", "切换飞行模式"), LocalizedText.Pick("Turns supported radios off or back on.", "关闭或重新开启支持的无线设备。"), "", ActionTag.System),
        new ActionOption(HotkeyActionType.LockWindows, LocalizedText.Pick("Lock Windows", "锁定 Windows"), LocalizedText.Pick("Locks the current Windows session.", "锁定当前 Windows 会话。"), "", ActionTag.System),
        new ActionOption(HotkeyActionType.Screenshot, LocalizedText.Pick("Take screenshot", "截图"), LocalizedText.Pick("Opens the native snipping overlay.", "打开系统截图浮层。"), "", ActionTag.System, ActionTag.Display),
        new ActionOption(HotkeyActionType.OpenCalculator, LocalizedText.Pick("Open Calculator", "打开计算器"), LocalizedText.Pick("Launches Calculator.", "启动计算器。"), "", ActionTag.System, ActionTag.Application),
        new ActionOption(HotkeyActionType.OpenApplication, LocalizedText.Pick("Open application", "打开应用"), LocalizedText.Pick("Launches an installed app, shortcut, or executable.", "启动已安装应用、快捷方式或可执行文件。"), "", ActionTag.Application)
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
        return TagOptions.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))?.Label ?? LocalizedText.Pick("Other", "其他");
    }
}

public static class MappingDisplayCatalog
{
    private const string OsdIconGlyph = "";

    public static string ShowOsdLabel => LocalizedText.Pick("Show OSD", "显示 OSD");

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
        new(OsdDisplayMode.IconAndText, LocalizedText.Pick("Icon + title", "图标 + 标题")),
        new(OsdDisplayMode.IconOnly, LocalizedText.Pick("Icon only", "仅图标")),
        new(OsdDisplayMode.TextOnly, LocalizedText.Pick("Title only", "仅标题"))
    ];
}

public static class DefaultKeyIds
{
    public const string FnLockOn = "key-fn-lock-on";
    public const string FnLockOff = "key-fn-lock-off";
    public const string CapsLockOn = "key-caps-lock-on";
    public const string CapsLockOff = "key-caps-lock-off";
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
