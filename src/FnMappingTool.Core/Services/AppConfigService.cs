using System.Text.Json;
using System.Text.Json.Serialization;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Core.Services;

public sealed class AppConfigService
{
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
            return NormalizeConfiguration(ReadConfiguration(ConfigPath), ConfigDirectory);
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
        try
        {
            return NormalizeConfiguration(ReadConfiguration(path), Path.GetDirectoryName(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("The selected JSON file is not a valid FnMappingTool configuration.", exception);
        }
    }

    public void Save(AppConfiguration configuration)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var normalized = NormalizeConfiguration(configuration, ConfigDirectory);
        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(ConfigPath, json + Environment.NewLine);
    }

    public void ImportFromFile(string path)
    {
        Save(LoadFromFile(path));
    }

    private static AppConfiguration ReadConfiguration(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions)
            ?? throw new InvalidOperationException("The configuration file is empty.");
    }

    private static AppConfiguration CreateDefaultConfiguration()
    {
        var configuration = AppConfiguration.CreateDefault();
        configuration.Keys = CreateDefaultKeys();
        configuration.Mappings = CreateDefaultMappings();
        return NormalizeConfiguration(configuration, baseDirectory: null);
    }

    private static AppConfiguration NormalizeConfiguration(AppConfiguration? configuration, string? baseDirectory)
    {
        configuration ??= new AppConfiguration();

        configuration.Theme = configuration.Theme switch
        {
            ThemePreference.Light => ThemePreference.Light,
            ThemePreference.Dark => ThemePreference.Dark,
            _ => ThemePreference.System
        };

        configuration.Preferences ??= new AppPreferences();
        configuration.Preferences.Osd ??= new OsdPreferences();
        configuration.Preferences.Osd.DisplayMode = configuration.Preferences.Osd.DisplayMode switch
        {
            OsdDisplayMode.IconOnly => OsdDisplayMode.IconOnly,
            OsdDisplayMode.TextOnly => OsdDisplayMode.TextOnly,
            _ => OsdDisplayMode.IconAndText
        };
        configuration.Preferences.Osd.DurationMs = Math.Clamp(
            configuration.Preferences.Osd.DurationMs <= 0 ? RuntimeDefaults.DefaultOsdDurationMs : configuration.Preferences.Osd.DurationMs,
            500,
            10000);
        configuration.Preferences.Osd.BackgroundOpacityPercent = Math.Clamp(
            configuration.Preferences.Osd.BackgroundOpacityPercent < 0 ? RuntimeDefaults.DefaultOsdBackgroundOpacityPercent : configuration.Preferences.Osd.BackgroundOpacityPercent,
            0,
            100);
        configuration.Preferences.Osd.ScalePercent = Math.Clamp(
            configuration.Preferences.Osd.ScalePercent <= 0 ? RuntimeDefaults.DefaultOsdScalePercent : configuration.Preferences.Osd.ScalePercent,
            60,
            200);

        configuration.Keys = (configuration.Keys ?? [])
            .Select(NormalizeKey)
            .ToList();

        var firstKeyId = configuration.Keys.FirstOrDefault()?.Id ?? string.Empty;
        var validKeyIds = configuration.Keys
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        configuration.Mappings = (configuration.Mappings ?? [])
            .Select(mapping => NormalizeMapping(mapping, configuration.Keys, validKeyIds, firstKeyId, baseDirectory))
            .ToList();

        return configuration;
    }

    private static KeyDefinitionConfiguration NormalizeKey(KeyDefinitionConfiguration? key)
    {
        key ??= new KeyDefinitionConfiguration();
        key.Id = NormalizeId(key.Id);
        key.Name = NormalizeName(key.Name, "Unnamed key");
        key.Trigger ??= new EventMatcherConfiguration();
        return key;
    }

    private static KeyActionMappingConfiguration NormalizeMapping(
        KeyActionMappingConfiguration? mapping,
        IReadOnlyList<KeyDefinitionConfiguration> keys,
        ISet<string> validKeyIds,
        string fallbackKeyId,
        string? baseDirectory)
    {
        mapping ??= new KeyActionMappingConfiguration();
        mapping.Id = NormalizeId(mapping.Id);
        mapping.KeyId = NormalizeOptional(mapping.KeyId) ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(fallbackKeyId) && !validKeyIds.Contains(mapping.KeyId))
        {
            mapping.KeyId = fallbackKeyId;
        }

        mapping.Action = NormalizeAction(mapping.Action, baseDirectory);
        var keyName = keys.FirstOrDefault(item => string.Equals(item.Id, mapping.KeyId, StringComparison.OrdinalIgnoreCase))?.Name;
        mapping.Name = NormalizeName(mapping.Name, DescribeMapping(keyName, mapping.Action.Type));
        return mapping;
    }

    private static ActionDefinitionConfiguration NormalizeAction(ActionDefinitionConfiguration? action, string? baseDirectory)
    {
        action ??= new ActionDefinitionConfiguration();
        action.Type = NormalizeActionType(action.Type);

        var target = NormalizeOptional(action.Target);
        var arguments = NormalizeOptional(action.Arguments);
        var osdTitle = NormalizeOptional(action.OsdTitle);
        if (!string.IsNullOrWhiteSpace(osdTitle) && osdTitle.Length > RuntimeDefaults.MaxOsdTitleLength)
        {
            osdTitle = osdTitle[..RuntimeDefaults.MaxOsdTitleLength];
        }

        var osdIcon = NormalizeIcon(action.OsdIcon, baseDirectory);

        action.Target = action.Type == HotkeyActionType.OpenApplication ? target : null;
        action.Arguments = action.Type == HotkeyActionType.OpenApplication ? arguments : null;
        action.OsdTitle = action.Type == HotkeyActionType.ShowOsd ? osdTitle : null;
        action.OsdIcon = action.Type == HotkeyActionType.ShowOsd ? osdIcon : new IconConfiguration();
        return action;
    }

    private static IconConfiguration NormalizeIcon(IconConfiguration? icon, string? baseDirectory)
    {
        var path = OsdIconPathResolver.NormalizeConfigPath(icon?.Path, baseDirectory);
        var isPng = !string.IsNullOrWhiteSpace(path);

        return new IconConfiguration
        {
            Mode = isPng ? IconSourceMode.CustomFile : IconSourceMode.None,
            Path = isPng ? path : null
        };
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
            CreateMapping("mapping-fn-lock-on", "Fn Lock On", DefaultKeyIds.FnLockOn, CreateOsdAction("Fn lock on", BuiltInOsdAsset.FnLock)),
            CreateMapping("mapping-fn-lock-off", "Fn Lock Off", DefaultKeyIds.FnLockOff, CreateOsdAction("Fn lock off", BuiltInOsdAsset.FnUnlock)),
            CreateMapping("mapping-mic-on", "Microphone Mute On", DefaultKeyIds.MicrophoneMuteOn, new ActionDefinitionConfiguration { Type = HotkeyActionType.MicrophoneMuteOn }),
            CreateMapping("mapping-mic-off", "Microphone Mute Off", DefaultKeyIds.MicrophoneMuteOff, new ActionDefinitionConfiguration { Type = HotkeyActionType.MicrophoneMuteOff }),
            CreateMapping("mapping-xiaoai-press", "XiaoAi Press", DefaultKeyIds.XiaoAiPress, new ActionDefinitionConfiguration { Type = HotkeyActionType.None }),
            CreateMapping("mapping-xiaoai-release", "XiaoAi Release", DefaultKeyIds.XiaoAiRelease, new ActionDefinitionConfiguration { Type = HotkeyActionType.None }),
            CreateMapping("mapping-settings", "Settings Press", DefaultKeyIds.SettingsPress, new ActionDefinitionConfiguration { Type = HotkeyActionType.OpenSettings }),
            CreateMapping("mapping-manager-press", "Manager Press", DefaultKeyIds.ManagerPress, new ActionDefinitionConfiguration { Type = HotkeyActionType.None }),
            CreateMapping("mapping-manager-release", "Manager Release", DefaultKeyIds.ManagerRelease, new ActionDefinitionConfiguration { Type = HotkeyActionType.None }),
            CreateMapping("mapping-backlight-off", "Backlight Off", DefaultKeyIds.BacklightOff, CreateOsdAction("Backlight off", BuiltInOsdAsset.BacklightOff)),
            CreateMapping("mapping-backlight-level1", "Backlight Level 1", DefaultKeyIds.BacklightLevel1, CreateOsdAction("Backlight level 1", BuiltInOsdAsset.BacklightLevel1)),
            CreateMapping("mapping-backlight-level2", "Backlight Level 2", DefaultKeyIds.BacklightLevel2, CreateOsdAction("Backlight level 2", BuiltInOsdAsset.BacklightLevel2)),
            CreateMapping("mapping-backlight-auto", "Backlight Auto", DefaultKeyIds.BacklightAuto, CreateOsdAction("Backlight auto", BuiltInOsdAsset.BacklightAuto)),
            CreateMapping("mapping-projection", "Projection UI", DefaultKeyIds.Projection, new ActionDefinitionConfiguration { Type = HotkeyActionType.OpenProjection })
        ];
    }

    private static KeyActionMappingConfiguration CreateMapping(string id, string keyName, string keyId, ActionDefinitionConfiguration action)
    {
        return new KeyActionMappingConfiguration
        {
            Id = id,
            Name = DescribeMapping(keyName, action.Type),
            Enabled = true,
            KeyId = keyId,
            Action = NormalizeAction(action, baseDirectory: null)
        };
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

    private static ActionDefinitionConfiguration CreateOsdAction(string title, string assetKey)
    {
        return new ActionDefinitionConfiguration
        {
            Type = HotkeyActionType.ShowOsd,
            OsdTitle = title,
            OsdIcon = new IconConfiguration
            {
                Mode = IconSourceMode.CustomFile,
                Path = assetKey + ".png"
            }
        };
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
        if (string.IsNullOrWhiteSpace(value))
        {
            return HotkeyActionType.None;
        }

        return ActionCatalog.All.FirstOrDefault(item => string.Equals(item.Key, value, StringComparison.OrdinalIgnoreCase))?.Key
            ?? HotkeyActionType.None;
    }
}
