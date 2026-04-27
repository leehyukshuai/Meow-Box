using System.Globalization;
using MeowBox.Core.Services;

namespace MeowBox.Core.Models;

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
