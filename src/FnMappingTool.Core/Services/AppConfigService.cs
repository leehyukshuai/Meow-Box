using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Core.Services;

public sealed class AppConfigService
{
    private const int IoRetryCount = 8;
    private const int IoRetryDelayMs = 40;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ConfigDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FnMappingTool");

    public string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public string? GetStoredLanguagePreference()
    {
        if (!File.Exists(ConfigPath))
        {
            return null;
        }

        try
        {
            var json = ExecuteWithRetries(() =>
            {
                using var stream = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            });

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Preferences", out var preferencesElement))
            {
                return null;
            }

            if (!preferencesElement.TryGetProperty("Language", out var languageElement))
            {
                return null;
            }

            return languageElement.GetString();
        }
        catch
        {
            return null;
        }
    }

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
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            var fallback = CreateDefaultConfiguration();
            Save(fallback);
            return fallback;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CreateDefaultConfiguration();
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
        WriteConfiguration(ConfigPath, json + Environment.NewLine);
    }

    public void ImportFromFile(string path)
    {
        Save(LoadFromFile(path));
    }

    private static AppConfiguration ReadConfiguration(string path)
    {
        var json = ExecuteWithRetries(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        });

        return JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions)
            ?? throw new InvalidOperationException("The configuration file is empty.");
    }

    private static void WriteConfiguration(string path, string contents)
    {
        ExecuteWithRetries(() =>
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(contents);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }
        });
    }

    private static T ExecuteWithRetries<T>(Func<T> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt >= IoRetryCount - 1)
                {
                    throw;
                }

                Thread.Sleep(IoRetryDelayMs * (attempt + 1));
            }
        }
    }

    private static void ExecuteWithRetries(Action action)
    {
        ExecuteWithRetries(() =>
        {
            action();
            return true;
        });
    }

    private static AppConfiguration CreateDefaultConfiguration()
    {
        return NormalizeConfiguration(AppConfiguration.CreateDefault(), baseDirectory: null);
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
        configuration.Preferences.Language = configuration.Preferences.Language switch
        {
            AppLanguagePreference.English => AppLanguagePreference.English,
            AppLanguagePreference.Chinese => AppLanguagePreference.Chinese,
            _ => AppLanguagePreference.System
        };
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
        key.Name = NormalizeName(key.Name, LocalizedText.Pick("Unnamed key", "未命名按键"));
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

        var legacyAction = mapping.Action ?? new ActionDefinitionConfiguration();
        var legacyShowOsd = string.Equals(legacyAction.Type, HotkeyActionType.ShowOsd, StringComparison.OrdinalIgnoreCase);

        mapping.Action = NormalizeAction(legacyAction, baseDirectory);
        mapping.Osd = NormalizeMappingOsd(mapping.Osd, legacyAction, legacyShowOsd, baseDirectory);

        var keyName = keys.FirstOrDefault(item => string.Equals(item.Id, mapping.KeyId, StringComparison.OrdinalIgnoreCase))?.Name;
        mapping.Name = DescribeMapping(keyName, mapping.Action.Type, mapping.Osd);
        return mapping;
    }

    private static ActionDefinitionConfiguration NormalizeAction(ActionDefinitionConfiguration? action, string? baseDirectory)
    {
        action ??= new ActionDefinitionConfiguration();
        action.Type = NormalizeActionType(action.Type);

        var target = NormalizeOptional(action.Target);
        var arguments = NormalizeOptional(action.Arguments);

        action.Target = action.Type == HotkeyActionType.OpenApplication ? target : null;
        action.Arguments = action.Type == HotkeyActionType.OpenApplication ? arguments : null;
        return action;
    }

    private static MappingOsdConfiguration NormalizeMappingOsd(
        MappingOsdConfiguration? osd,
        ActionDefinitionConfiguration legacyAction,
        bool legacyShowOsd,
        string? baseDirectory)
    {
        osd ??= new MappingOsdConfiguration();

        var title = NormalizeOptional(osd.Title);
        if (string.IsNullOrWhiteSpace(title) && legacyShowOsd)
        {
            title = NormalizeOptional(legacyAction.OsdTitle);
        }

        if (!string.IsNullOrWhiteSpace(title) && title.Length > RuntimeDefaults.MaxOsdTitleLength)
        {
            title = title[..RuntimeDefaults.MaxOsdTitleLength];
        }

        var icon = NormalizeIcon(osd.Icon, baseDirectory);
        if (string.IsNullOrWhiteSpace(icon.Path) && legacyShowOsd)
        {
            icon = NormalizeIcon(legacyAction.OsdIcon, baseDirectory);
        }

        return new MappingOsdConfiguration
        {
            Enabled = osd.Enabled || legacyShowOsd,
            Title = title,
            Icon = icon
        };
    }

    private static IconConfiguration NormalizeIcon(IconConfiguration? icon, string? baseDirectory)
    {
        var path = NormalizeBuiltInOsdIconPath(icon?.Path);
        path = OsdIconPathResolver.NormalizeConfigPath(path, baseDirectory);
        var isPng = !string.IsNullOrWhiteSpace(path);

        return new IconConfiguration
        {
            Mode = isPng ? IconSourceMode.CustomFile : IconSourceMode.None,
            Path = isPng ? path : null
        };
    }

    private static string DescribeMapping(string? keyName, string actionType, MappingOsdConfiguration osd)
    {
        var keyLabel = string.IsNullOrWhiteSpace(keyName) ? LocalizedText.Pick("Select key", "选择按键") : keyName.Trim();
        return keyLabel;
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
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, HotkeyActionType.ShowOsd, StringComparison.OrdinalIgnoreCase))
        {
            return HotkeyActionType.None;
        }

        return ActionCatalog.All.FirstOrDefault(item => string.Equals(item.Key, value, StringComparison.OrdinalIgnoreCase))?.Key
            ?? HotkeyActionType.None;
    }

    private static string? NormalizeBuiltInOsdIconPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Path.GetFileName(path).ToLowerInvariant() switch
        {
            "backlight-level1.png" => ReplaceIconFileName(path, "backlight-low.png"),
            "backlight-level2.png" => ReplaceIconFileName(path, "backlight-high.png"),
            _ => path
        };
    }

    private static string ReplaceIconFileName(string originalPath, string newFileName)
    {
        var directory = Path.GetDirectoryName(originalPath);
        return string.IsNullOrWhiteSpace(directory)
            ? newFileName
            : Path.Combine(directory, newFileName);
    }
}
