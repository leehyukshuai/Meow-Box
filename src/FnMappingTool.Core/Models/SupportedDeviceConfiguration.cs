namespace FnMappingTool.Core.Models;

public static class SupportedDeviceConfiguration
{
    public const string DeviceDisplayName = "Xiaomi Book Pro 14 2026";

    public static AppConfiguration CreateDefault()
    {
        return new AppConfiguration
        {
            Theme = ThemePreference.System,
            Preferences = new AppPreferences
            {
                IsListening = true,
                PreferPriorityStartup = false,
                Language = AppLanguagePreference.System,
                ShowTrayIcon = true,
                Osd = new OsdPreferences
                {
                    DisplayMode = OsdDisplayMode.IconOnly,
                    DurationMs = RuntimeDefaults.DefaultOsdDurationMs,
                    BackgroundOpacityPercent = RuntimeDefaults.DefaultOsdBackgroundOpacityPercent,
                    ScalePercent = RuntimeDefaults.DefaultOsdScalePercent
                }
            },
            Touchpad = new TouchpadConfiguration
            {
                Enabled = true,
                DeepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold,
                SurfaceWidth = RuntimeDefaults.DefaultTouchpadSurfaceWidth,
                SurfaceHeight = RuntimeDefaults.DefaultTouchpadSurfaceHeight,
                DeepPressAction = new ActionDefinitionConfiguration
                {
                    Type = HotkeyActionType.None
                },
                LeftTopCorner = TouchpadCornerRegionConfiguration.CreateLeftTopDefault(),
                RightTopCorner = TouchpadCornerRegionConfiguration.CreateRightTopDefault()
            },
            Keys = CreateKeys(),
            Mappings = CreateMappings()
        };
    }

    public static List<KeyDefinitionConfiguration> CreateKeys()
    {
        return
        [
            CreateKey(DefaultKeyIds.ManagerPress, "PC Manager Press", "01-25-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.XiaoAiPress, "XiaoAi Press", "01-23-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.SettingsPress, "Settings Press", "01-1B-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.Projection, "Projection UI", "01-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.FnLockOn, "Fn Lock On", "01-07-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.FnLockOff, "Fn Lock Off", "01-07-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.CapsLockOn, "Caps Lock On", "01-09-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.CapsLockOff, "Caps Lock Off", "01-09-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.MicrophoneMuteOn, "Microphone Mute On", "01-21-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.MicrophoneMuteOff, "Microphone Mute Off", "01-21-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.BacklightOff, "Backlight Off", "01-05-00", requireWmiActive: false),
            CreateKey(DefaultKeyIds.BacklightLevel1, "Backlight Level 1", "01-05-05", requireWmiActive: false),
            CreateKey(DefaultKeyIds.BacklightLevel2, "Backlight Level 2", "01-05-0A", requireWmiActive: false),
            CreateKey(DefaultKeyIds.BacklightAuto, "Backlight Auto", "01-05-80", requireWmiActive: false)
        ];
    }

    public static List<KeyActionMappingConfiguration> CreateMappings()
    {
        return
        [
            CreateMapping("mapping-manager-press", DefaultKeyIds.ManagerPress, "PC Manager Press", HotkeyActionType.None, CreateOsd("PC Manager")),
            CreateMapping("mapping-xiaoai-press", DefaultKeyIds.XiaoAiPress, "XiaoAi Press", HotkeyActionType.None, CreateOsd("XiaoAi")),
            CreateMapping("mapping-settings", DefaultKeyIds.SettingsPress, "Settings Press", HotkeyActionType.None, CreateOsd("Settings")),
            CreateMapping("mapping-projection", DefaultKeyIds.Projection, "Projection UI", HotkeyActionType.None, CreateOsd("Projection")),
            CreateMapping("mapping-fn-lock-on", DefaultKeyIds.FnLockOn, "Fn Lock On", HotkeyActionType.None, CreateOsd("Fn lock on", "fn-lock.png")),
            CreateMapping("mapping-fn-lock-off", DefaultKeyIds.FnLockOff, "Fn Lock Off", HotkeyActionType.None, CreateOsd("Fn lock off", "fn-unlock.png")),
            CreateMapping("mapping-caps-lock-on", DefaultKeyIds.CapsLockOn, "Caps Lock On", HotkeyActionType.None, CreateOsd("Caps lock on", "caps-lock.png")),
            CreateMapping("mapping-caps-lock-off", DefaultKeyIds.CapsLockOff, "Caps Lock Off", HotkeyActionType.None, CreateOsd("Caps lock off", "caps-unlock.png")),
            CreateMapping("mapping-mic-on", DefaultKeyIds.MicrophoneMuteOn, "Microphone Mute On", HotkeyActionType.MicrophoneMuteOn, CreateOsd("Microphone off", "microphone-mute.png")),
            CreateMapping("mapping-mic-off", DefaultKeyIds.MicrophoneMuteOff, "Microphone Mute Off", HotkeyActionType.MicrophoneMuteOff, CreateOsd("Microphone on", "microphone-on.png")),
            CreateMapping("mapping-backlight-off", DefaultKeyIds.BacklightOff, "Backlight Off", HotkeyActionType.None, CreateOsd("Backlight off", "backlight-off.png")),
            CreateMapping("mapping-backlight-level1", DefaultKeyIds.BacklightLevel1, "Backlight Level 1", HotkeyActionType.None, CreateOsd("Backlight low", "backlight-low.png")),
            CreateMapping("mapping-backlight-level2", DefaultKeyIds.BacklightLevel2, "Backlight Level 2", HotkeyActionType.None, CreateOsd("Backlight high", "backlight-high.png")),
            CreateMapping("mapping-backlight-auto", DefaultKeyIds.BacklightAuto, "Backlight Auto", HotkeyActionType.None, CreateOsd("Backlight auto", "backlight-auto.png"))
        ];
    }

    private static KeyDefinitionConfiguration CreateKey(string id, string name, string reportHex, bool requireWmiActive = true)
    {
        return new KeyDefinitionConfiguration
        {
            Id = id,
            Name = name,
            Trigger = new EventMatcherConfiguration
            {
                Source = InputSourceKind.Wmi,
                WmiClassName = "HID_EVENT20",
                WmiActive = requireWmiActive ? true : null,
                ReportHex = reportHex
            }
        };
    }

    private static KeyActionMappingConfiguration CreateMapping(
        string id,
        string keyId,
        string name,
        string actionType,
        MappingOsdConfiguration? osd = null,
        bool enabled = true)
    {
        return new KeyActionMappingConfiguration
        {
            Id = id,
            Name = name,
            Enabled = enabled,
            KeyId = keyId,
            Action = new ActionDefinitionConfiguration
            {
                Type = actionType
            },
            Osd = osd ?? new MappingOsdConfiguration()
        };
    }

    public static bool ShouldUseOsdOnlyDefault(string keyId)
    {
        return !string.Equals(keyId, DefaultKeyIds.MicrophoneMuteOn, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(keyId, DefaultKeyIds.MicrophoneMuteOff, StringComparison.OrdinalIgnoreCase);
    }

    private static MappingOsdConfiguration CreateOsd(string title, string? iconPath = null)
    {
        return new MappingOsdConfiguration
        {
            Enabled = true,
            Title = title,
            Icon = new IconConfiguration
            {
                Mode = string.IsNullOrWhiteSpace(iconPath) ? IconSourceMode.None : IconSourceMode.CustomFile,
                Path = string.IsNullOrWhiteSpace(iconPath) ? null : iconPath
            }
        };
    }
}
