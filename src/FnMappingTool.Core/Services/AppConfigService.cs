using System.Text.Json;
using System.Text.Json.Serialization;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Core.Services;

public sealed class AppConfigService
{
    private const string LegacySwitchTrayIconActionType = "SwitchTrayIcon";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ConfigDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FnMappingTool");

    public string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public AppConfiguration Load()
    {
        Directory.CreateDirectory(ConfigDirectory);

        if (!File.Exists(ConfigPath))
        {
            var created = CreateDefaultConfiguration();
            Save(created);
            return created;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var configuration = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions) ?? CreateDefaultConfiguration();
            MergeDefaults(configuration);
            return configuration;
        }
        catch
        {
            var fallback = CreateDefaultConfiguration();
            Save(fallback);
            return fallback;
        }
    }

    public AppConfiguration LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var configuration = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions) ?? CreateDefaultConfiguration();
        MergeDefaults(configuration);
        return configuration;
    }

    public void Save(AppConfiguration configuration)
    {
        Directory.CreateDirectory(ConfigDirectory);
        MergeDefaults(configuration);
        configuration.Actions = null;
        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        File.WriteAllText(ConfigPath, json + Environment.NewLine);
    }

    public void ImportFromFile(string path)
    {
        Save(LoadFromFile(path));
    }

    private static AppConfiguration CreateDefaultConfiguration()
    {
        var configuration = AppConfiguration.CreateDefault();
        configuration.Keys = CreateDefaultKeys();
        configuration.Mappings = CreateDefaultMappings();
        configuration.Actions = null;
        return configuration;
    }

    private static void MergeDefaults(AppConfiguration configuration)
    {
        configuration.Theme = configuration.Theme switch
        {
            ThemePreference.Light => ThemePreference.Light,
            ThemePreference.Dark => ThemePreference.Dark,
            _ => ThemePreference.System
        };

        configuration.Preferences ??= new AppPreferences();
        configuration.Keys ??= [];
        configuration.Mappings ??= [];

        var legacyActions = configuration.Actions ?? [];

        if (configuration.Keys.Count == 0)
        {
            configuration.Keys.AddRange(CreateDefaultKeys());
        }

        foreach (var key in configuration.Keys)
        {
            key.Id = NormalizeId(key.Id);
            key.Name = NormalizeName(key.Name, "Unnamed key");
            key.Trigger ??= new EventMatcherConfiguration();
        }

        EnsureDefaultKeys(configuration.Keys);

        if (configuration.Mappings.Count == 0)
        {
            configuration.Mappings.AddRange(CreateDefaultMappings());
        }

        foreach (var mapping in configuration.Mappings)
        {
            mapping.Id = NormalizeId(mapping.Id);
            mapping.Name = NormalizeName(mapping.Name, "Unnamed mapping");
            mapping.KeyId = NormalizeOptional(mapping.KeyId) ?? string.Empty;
            mapping.ActionId = NormalizeOptional(mapping.ActionId);
            mapping.Action ??= ResolveLegacyAction(mapping.ActionId, legacyActions) ?? new ActionDefinitionConfiguration
            {
                Type = HotkeyActionType.None
            };
            NormalizeAction(mapping.Action);
        }

        EnsureDefaultMappings(configuration);

        var validKeyIds = configuration.Keys.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in configuration.Mappings)
        {
            if (!validKeyIds.Contains(mapping.KeyId))
            {
                mapping.KeyId = configuration.Keys[0].Id;
            }

            mapping.ActionId = null;
            mapping.Name = NormalizeName(mapping.Name, DescribeMapping(
                configuration.Keys.FirstOrDefault(item => string.Equals(item.Id, mapping.KeyId, StringComparison.OrdinalIgnoreCase))?.Name,
                mapping.Action.Type));
        }

        configuration.Actions = null;
    }

    private static void EnsureDefaultKeys(List<KeyDefinitionConfiguration> keys)
    {
        UpsertKey(keys, DefaultKeyIds.FnLockOn, "Fn Lock On", Wmi("HID_EVENT20", true, "01-07-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.FnLockOff, "Fn Lock Off", Wmi("HID_EVENT20", true, "01-07-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.MicrophoneMuteOn, "Microphone Mute On", Wmi("HID_EVENT20", true, "01-21-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.MicrophoneMuteOff, "Microphone Mute Off", Wmi("HID_EVENT20", true, "01-21-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.XiaoAiPress, "XiaoAi Press", Wmi("HID_EVENT20", true, "01-23-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.XiaoAiRelease, "XiaoAi Release", Wmi("HID_EVENT20", true, "01-24-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.SettingsPress, "Settings Press", Wmi("HID_EVENT20", true, "01-1B-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.ManagerPress, "Manager Press", Wmi("HID_EVENT20", true, "01-25-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.ManagerRelease, "Manager Release", Wmi("HID_EVENT20", true, "01-26-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.BacklightOff, "Backlight Off", Wmi("HID_EVENT20", true, "01-05-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.BacklightLevel1, "Backlight Level 1", Wmi("HID_EVENT20", true, "01-05-05-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.BacklightLevel2, "Backlight Level 2", Wmi("HID_EVENT20", true, "01-05-0A-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.BacklightAuto, "Backlight Auto", Wmi("HID_EVENT20", true, "01-05-80-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
        UpsertKey(keys, DefaultKeyIds.Projection, "Projection UI", Wmi("HID_EVENT20", true, "01-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00"));
    }

    private static void EnsureDefaultMappings(AppConfiguration configuration)
    {
        UpsertMapping(configuration.Mappings, "mapping-fn-lock-on", DefaultKeyIds.FnLockOn, OsdAction("Fn lock on", BuiltInOsdAsset.FnLock));
        UpsertMapping(configuration.Mappings, "mapping-fn-lock-off", DefaultKeyIds.FnLockOff, OsdAction("Fn lock off", BuiltInOsdAsset.FnUnlock));
        UpsertMapping(configuration.Mappings, "mapping-mic-on", DefaultKeyIds.MicrophoneMuteOn, new ActionDefinitionConfiguration { Type = HotkeyActionType.MicrophoneMuteOn });
        UpsertMapping(configuration.Mappings, "mapping-mic-off", DefaultKeyIds.MicrophoneMuteOff, new ActionDefinitionConfiguration { Type = HotkeyActionType.MicrophoneMuteOff });
        UpsertMapping(configuration.Mappings, "mapping-xiaoai-press", DefaultKeyIds.XiaoAiPress, new ActionDefinitionConfiguration { Type = HotkeyActionType.None });
        UpsertMapping(configuration.Mappings, "mapping-xiaoai-release", DefaultKeyIds.XiaoAiRelease, new ActionDefinitionConfiguration { Type = HotkeyActionType.None });
        UpsertMapping(configuration.Mappings, "mapping-settings", DefaultKeyIds.SettingsPress, new ActionDefinitionConfiguration { Type = HotkeyActionType.OpenSettings });
        UpsertMapping(configuration.Mappings, "mapping-manager-press", DefaultKeyIds.ManagerPress, new ActionDefinitionConfiguration { Type = HotkeyActionType.None });
        UpsertMapping(configuration.Mappings, "mapping-manager-release", DefaultKeyIds.ManagerRelease, new ActionDefinitionConfiguration { Type = HotkeyActionType.None });
        UpsertMapping(configuration.Mappings, "mapping-backlight-off", DefaultKeyIds.BacklightOff, OsdAction("Backlight off", BuiltInOsdAsset.BacklightOff));
        UpsertMapping(configuration.Mappings, "mapping-backlight-level1", DefaultKeyIds.BacklightLevel1, OsdAction("Backlight level 1", BuiltInOsdAsset.BacklightLevel1));
        UpsertMapping(configuration.Mappings, "mapping-backlight-level2", DefaultKeyIds.BacklightLevel2, OsdAction("Backlight level 2", BuiltInOsdAsset.BacklightLevel2));
        UpsertMapping(configuration.Mappings, "mapping-backlight-auto", DefaultKeyIds.BacklightAuto, OsdAction("Backlight auto", BuiltInOsdAsset.BacklightAuto));
        UpsertMapping(configuration.Mappings, "mapping-projection", DefaultKeyIds.Projection, new ActionDefinitionConfiguration { Type = HotkeyActionType.OpenProjection });
    }

    private static List<KeyDefinitionConfiguration> CreateDefaultKeys()
    {
        return
        [
            new() { Id = DefaultKeyIds.FnLockOn, Name = "Fn Lock On", Trigger = Wmi("HID_EVENT20", true, "01-07-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.FnLockOff, Name = "Fn Lock Off", Trigger = Wmi("HID_EVENT20", true, "01-07-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.MicrophoneMuteOn, Name = "Microphone Mute On", Trigger = Wmi("HID_EVENT20", true, "01-21-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.MicrophoneMuteOff, Name = "Microphone Mute Off", Trigger = Wmi("HID_EVENT20", true, "01-21-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.XiaoAiPress, Name = "XiaoAi Press", Trigger = Wmi("HID_EVENT20", true, "01-23-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.XiaoAiRelease, Name = "XiaoAi Release", Trigger = Wmi("HID_EVENT20", true, "01-24-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.SettingsPress, Name = "Settings Press", Trigger = Wmi("HID_EVENT20", true, "01-1B-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.ManagerPress, Name = "Manager Press", Trigger = Wmi("HID_EVENT20", true, "01-25-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.ManagerRelease, Name = "Manager Release", Trigger = Wmi("HID_EVENT20", true, "01-26-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.BacklightOff, Name = "Backlight Off", Trigger = Wmi("HID_EVENT20", true, "01-05-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.BacklightLevel1, Name = "Backlight Level 1", Trigger = Wmi("HID_EVENT20", true, "01-05-05-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.BacklightLevel2, Name = "Backlight Level 2", Trigger = Wmi("HID_EVENT20", true, "01-05-0A-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.BacklightAuto, Name = "Backlight Auto", Trigger = Wmi("HID_EVENT20", true, "01-05-80-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") },
            new() { Id = DefaultKeyIds.Projection, Name = "Projection UI", Trigger = Wmi("HID_EVENT20", true, "01-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00") }
        ];
    }

    private static List<KeyActionMappingConfiguration> CreateDefaultMappings()
    {
        return
        [
            new() { Id = "mapping-fn-lock-on", Name = "Fn Lock On -> Show OSD", Enabled = true, KeyId = DefaultKeyIds.FnLockOn, Action = OsdAction("Fn lock on", BuiltInOsdAsset.FnLock) },
            new() { Id = "mapping-fn-lock-off", Name = "Fn Lock Off -> Show OSD", Enabled = true, KeyId = DefaultKeyIds.FnLockOff, Action = OsdAction("Fn lock off", BuiltInOsdAsset.FnUnlock) },
            new() { Id = "mapping-mic-on", Name = "Microphone Mute On -> Mute microphone input", Enabled = true, KeyId = DefaultKeyIds.MicrophoneMuteOn, Action = new ActionDefinitionConfiguration { Type = HotkeyActionType.MicrophoneMuteOn } },
            new() { Id = "mapping-mic-off", Name = "Microphone Mute Off -> Unmute microphone input", Enabled = true, KeyId = DefaultKeyIds.MicrophoneMuteOff, Action = new ActionDefinitionConfiguration { Type = HotkeyActionType.MicrophoneMuteOff } },
            new() { Id = "mapping-xiaoai-press", Name = "XiaoAi Press -> Select action", Enabled = true, KeyId = DefaultKeyIds.XiaoAiPress, Action = new ActionDefinitionConfiguration { Type = HotkeyActionType.None } },
            new() { Id = "mapping-xiaoai-release", Name = "XiaoAi Release -> Select action", Enabled = true, KeyId = DefaultKeyIds.XiaoAiRelease, Action = new ActionDefinitionConfiguration { Type = HotkeyActionType.None } },
            new() { Id = "mapping-settings", Name = "Settings Press -> Open Windows Settings", Enabled = true, KeyId = DefaultKeyIds.SettingsPress, Action = new ActionDefinitionConfiguration { Type = HotkeyActionType.OpenSettings } },
            new() { Id = "mapping-manager-press", Name = "Manager Press -> Select action", Enabled = true, KeyId = DefaultKeyIds.ManagerPress, Action = new ActionDefinitionConfiguration { Type = HotkeyActionType.None } },
            new() { Id = "mapping-manager-release", Name = "Manager Release -> Select action", Enabled = true, KeyId = DefaultKeyIds.ManagerRelease, Action = new ActionDefinitionConfiguration { Type = HotkeyActionType.None } },
            new() { Id = "mapping-backlight-off", Name = "Backlight Off -> Show OSD", Enabled = true, KeyId = DefaultKeyIds.BacklightOff, Action = OsdAction("Backlight off", BuiltInOsdAsset.BacklightOff) },
            new() { Id = "mapping-backlight-level1", Name = "Backlight Level 1 -> Show OSD", Enabled = true, KeyId = DefaultKeyIds.BacklightLevel1, Action = OsdAction("Backlight level 1", BuiltInOsdAsset.BacklightLevel1) },
            new() { Id = "mapping-backlight-level2", Name = "Backlight Level 2 -> Show OSD", Enabled = true, KeyId = DefaultKeyIds.BacklightLevel2, Action = OsdAction("Backlight level 2", BuiltInOsdAsset.BacklightLevel2) },
            new() { Id = "mapping-backlight-auto", Name = "Backlight Auto -> Show OSD", Enabled = true, KeyId = DefaultKeyIds.BacklightAuto, Action = OsdAction("Backlight auto", BuiltInOsdAsset.BacklightAuto) },
            new() { Id = "mapping-projection", Name = "Projection UI -> Open projection switcher", Enabled = true, KeyId = DefaultKeyIds.Projection, Action = new ActionDefinitionConfiguration { Type = HotkeyActionType.OpenProjection } }
        ];
    }

    private static void UpsertKey(List<KeyDefinitionConfiguration> keys, string id, string name, EventMatcherConfiguration trigger)
    {
        var existing = keys.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            keys.Add(new KeyDefinitionConfiguration
            {
                Id = id,
                Name = name,
                Trigger = trigger
            });
            return;
        }

        existing.Name = NormalizeName(existing.Name, name);
        existing.Trigger ??= trigger;
    }

    private static void UpsertMapping(List<KeyActionMappingConfiguration> mappings, string id, string keyId, ActionDefinitionConfiguration action)
    {
        var existing = mappings.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            mappings.Add(new KeyActionMappingConfiguration
            {
                Id = id,
                Enabled = true,
                KeyId = keyId,
                Action = CloneAction(action),
                Name = DescribeMapping(null, action.Type)
            });
            return;
        }

        if (string.IsNullOrWhiteSpace(existing.KeyId))
        {
            existing.KeyId = keyId;
        }

        existing.Action ??= ResolveLegacyAction(existing.ActionId, []) ?? CloneAction(action);
        NormalizeAction(existing.Action);
    }

    private static EventMatcherConfiguration Wmi(string className, bool active, string reportHex)
    {
        return new EventMatcherConfiguration
        {
            Source = InputSourceKind.Wmi,
            WmiClassName = className,
            WmiActive = active,
            ReportHex = reportHex
        };
    }

    private static ActionDefinitionConfiguration OsdAction(string title, string asset)
    {
        return new ActionDefinitionConfiguration
        {
            Type = HotkeyActionType.ShowOsd,
            OsdTitle = title,
            DurationMs = RuntimeDefaults.DefaultOsdDurationMs,
            OsdIcon = new IconConfiguration
            {
                Mode = IconSourceMode.BuiltIn,
                BuiltInAsset = asset
            }
        };
    }

    private static ActionDefinitionConfiguration? ResolveLegacyAction(string? actionId, IEnumerable<ActionDefinitionConfiguration> actions)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return null;
        }

        return actions.FirstOrDefault(item => string.Equals(item.Id, actionId, StringComparison.OrdinalIgnoreCase)) is { } resolved
            ? CloneAction(resolved)
            : null;
    }

    private static ActionDefinitionConfiguration CloneAction(ActionDefinitionConfiguration action)
    {
        return new ActionDefinitionConfiguration
        {
            Id = NormalizeOptional(action.Id),
            Type = NormalizeActionType(action.Type),
            Target = NormalizeOptional(action.Target),
            Arguments = NormalizeOptional(action.Arguments),
            OsdTitle = NormalizeOptional(action.OsdTitle),
            OsdMessage = NormalizeOptional(action.OsdMessage),
            DurationMs = action.DurationMs.HasValue ? Math.Max(500, action.DurationMs.Value) : null,
            OsdIcon = CloneIcon(action.OsdIcon),
            TrayIcon = CloneIcon(action.TrayIcon)
        };
    }

    private static IconConfiguration CloneIcon(IconConfiguration? icon)
    {
        return new IconConfiguration
        {
            Mode = icon?.Mode ?? IconSourceMode.None,
            BuiltInAsset = NormalizeOptional(icon?.BuiltInAsset),
            Path = NormalizeOptional(icon?.Path)
        };
    }

    private static void NormalizeAction(ActionDefinitionConfiguration action)
    {
        if (string.Equals(action.Type, LegacySwitchTrayIconActionType, StringComparison.OrdinalIgnoreCase))
        {
            MigrateLegacyTrayActionToOsd(action);
        }

        action.Type = NormalizeActionType(action.Type);
        action.Target = NormalizeOptional(action.Target);
        action.Arguments = NormalizeOptional(action.Arguments);
        action.OsdTitle = NormalizeOptional(action.OsdTitle);
        action.OsdMessage = NormalizeOptional(action.OsdMessage);
        action.DurationMs = action.DurationMs.HasValue ? Math.Max(500, action.DurationMs.Value) : null;
        action.OsdIcon ??= new IconConfiguration();
        action.TrayIcon ??= new IconConfiguration();
        NormalizeIcon(action.OsdIcon);
        NormalizeIcon(action.TrayIcon);
    }

    private static void MigrateLegacyTrayActionToOsd(ActionDefinitionConfiguration action)
    {
        var assetKey = NormalizeOptional(action.TrayIcon?.BuiltInAsset) ?? BuiltInOsdAsset.FnUnlock;
        action.Type = HotkeyActionType.ShowOsd;
        action.OsdTitle ??= IconAssetCatalog.GetLabel(assetKey);
        action.DurationMs ??= RuntimeDefaults.DefaultOsdDurationMs;
        action.OsdIcon ??= new IconConfiguration();
        action.OsdIcon.Mode = IconSourceMode.BuiltIn;
        action.OsdIcon.BuiltInAsset = assetKey;
        action.TrayIcon = new IconConfiguration();
    }

    private static string DescribeMapping(string? keyName, string actionType)
    {
        var keyLabel = string.IsNullOrWhiteSpace(keyName) ? "Select key" : keyName.Trim();
        return keyLabel + " -> " + ActionCatalog.GetLabel(actionType);
    }

    private static string NormalizeId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim();
    }

    private static string NormalizeName(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeActionType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            return HotkeyActionType.None;
        }

        return ActionCatalog.All.Any(item => string.Equals(item.Key, value, StringComparison.OrdinalIgnoreCase))
            ? value!
            : HotkeyActionType.None;
    }

    private static void NormalizeIcon(IconConfiguration icon)
    {
        icon.Mode = icon.Mode switch
        {
            IconSourceMode.BuiltIn => IconSourceMode.BuiltIn,
            IconSourceMode.CustomFile => IconSourceMode.CustomFile,
            _ => IconSourceMode.None
        };

        icon.BuiltInAsset = NormalizeOptional(icon.BuiltInAsset);
        icon.Path = NormalizeOptional(icon.Path);
    }
}
