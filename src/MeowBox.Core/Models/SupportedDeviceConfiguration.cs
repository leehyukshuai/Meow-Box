namespace MeowBox.Core.Models;

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
                PreferPriorityStartup = true,
                SwitchToBatteryModeOnDcThresholdPercent = BatteryControlCatalog.AutoSwitchAlwaysThreshold,
                PreferredPerformanceModeKey = BatteryControlCatalog.Smart,
                PerformanceModeCycleKeys =
                [
                    BatteryControlCatalog.Smart,
                    BatteryControlCatalog.Silent,
                    BatteryControlCatalog.Extreme
                ],
                ResetChargeLimitToFullOnStartup = false,
                PreferredChargeLimitPercent = 90,
                Language = AppLanguagePreference.System,
                ShowTrayIcon = true,
                Osd = new OsdPreferences
                {
                    DisplayMode = OsdDisplayMode.IconOnly,
                    DurationMs = 800,
                    BackgroundOpacityPercent = 20,
                    ScalePercent = 65
                }
            },
            Touchpad = new TouchpadConfiguration
            {
                Enabled = true,
                LightPressThreshold = 125,
                PressSensitivityLevel = 2,
                DeepPressThreshold = 500,
                LongPressDurationMs = 750,
                FeedbackLevel = 2,
                DeepPressHapticsEnabled = true,
                EdgeSlideEnabled = true,
                SurfaceWidth = 3282,
                SurfaceHeight = 2124,
                DeepPressAction = new ActionDefinitionConfiguration
                {
                    Type = HotkeyActionType.None
                },
                FiveFingerPinchInAction = new ActionDefinitionConfiguration
                {
                    Type = HotkeyActionType.None
                },
                FiveFingerPinchOutAction = new ActionDefinitionConfiguration
                {
                    Type = HotkeyActionType.None
                },
                LeftEdgeSlideAction = new ActionDefinitionConfiguration
                {
                    Type = HotkeyActionType.BrightnessUp
                },
                RightEdgeSlideAction = new ActionDefinitionConfiguration
                {
                    Type = HotkeyActionType.VolumeUp
                },
                LeftTopCorner = TouchpadCornerRegionConfiguration.CreateLeftTopDefault(),
                RightTopCorner = TouchpadCornerRegionConfiguration.CreateRightTopDefault()
            },
            Keys = CreateCustomizableKeys(),
            Mappings = CreateCustomizableMappings()
        };
    }

    public static List<KeyDefinitionConfiguration> CreateCustomizableKeys()
    {
        return
        [
            CreateKey(DefaultKeyIds.ManagerPress, "PC Manager Press", "01-25-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.XiaoAiPress, "XiaoAi Press", "01-23-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.SettingsPress, "Settings Press", "01-1B-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.Projection, "Projection UI", "01-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.PerformanceModePress, "Fn + K Performance Mode", "01-28-01", requireWmiActive: false)
        ];
    }

    public static List<KeyActionMappingConfiguration> CreateCustomizableMappings()
    {
        return
        [
            CreateMapping("mapping-manager-press", DefaultKeyIds.ManagerPress, "PC Manager Press", HotkeyActionType.None, enabled: false),
            CreateMapping("mapping-xiaoai-press", DefaultKeyIds.XiaoAiPress, "XiaoAi Press", HotkeyActionType.None),
            CreateMapping("mapping-settings", DefaultKeyIds.SettingsPress, "Settings Press", HotkeyActionType.OpenSettings),
            CreateMapping("mapping-projection", DefaultKeyIds.Projection, "Projection UI", HotkeyActionType.OpenProjection),
            CreateMapping("mapping-performance-mode-press", DefaultKeyIds.PerformanceModePress, "Fn + K Performance Mode", HotkeyActionType.CyclePerformanceMode)
        ];
    }

    public static List<KeyDefinitionConfiguration> CreateBuiltInRuntimeKeys()
    {
        return
        [
            CreateKey(DefaultKeyIds.PerformanceModePress, "Fn + K Performance Mode", "01-28-01", requireWmiActive: false),
            CreateKey(DefaultKeyIds.FnLockToggle, "Fn Lock Toggle", "01-07", requireWmiActive: false),
            CreateKey(DefaultKeyIds.CapsLockToggle, "Caps Lock Toggle", "01-09", requireWmiActive: false),
            CreateKey(DefaultKeyIds.MicrophoneMuteOn, "Microphone Mute On", "01-21-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.MicrophoneMuteOff, "Microphone Mute Off", "01-21-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"),
            CreateKey(DefaultKeyIds.BacklightCycle, "Keyboard Backlight", "01-05", requireWmiActive: false)
        ];
    }

    public static List<KeyActionMappingConfiguration> CreateBuiltInRuntimeMappings()
    {
        return
        [
            CreateMapping("mapping-performance-mode-press", DefaultKeyIds.PerformanceModePress, "Fn + K Performance Mode", HotkeyActionType.CyclePerformanceMode),
            CreateMapping("mapping-fn-lock-toggle", DefaultKeyIds.FnLockToggle, "Fn Lock Toggle", HotkeyActionType.ShowFnLockOsd),
            CreateMapping("mapping-caps-lock-toggle", DefaultKeyIds.CapsLockToggle, "Caps Lock Toggle", HotkeyActionType.ShowCapsLockOsd),
            CreateMapping("mapping-mic-on", DefaultKeyIds.MicrophoneMuteOn, "Microphone Mute On", HotkeyActionType.MicrophoneMuteOn),
            CreateMapping("mapping-mic-off", DefaultKeyIds.MicrophoneMuteOff, "Microphone Mute Off", HotkeyActionType.MicrophoneMuteOff),
            CreateMapping("mapping-backlight-cycle", DefaultKeyIds.BacklightCycle, "Keyboard Backlight", HotkeyActionType.ShowKeyboardBacklightOsd)
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
            }
        };
    }

    public static bool ShouldRemainEnabledWithoutAssignedAction(string keyId)
    {
        return string.Equals(keyId, DefaultKeyIds.XiaoAiPress, StringComparison.OrdinalIgnoreCase);
    }

}
