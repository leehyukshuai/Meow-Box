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

    public void Save(AppConfiguration configuration)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var normalized = NormalizeConfiguration(configuration, ConfigDirectory);
        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        WriteConfiguration(ConfigPath, json + Environment.NewLine);
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
        var supported = SupportedDeviceConfiguration.CreateDefault();

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
        configuration.Touchpad = NormalizeTouchpadConfiguration(configuration.Touchpad, baseDirectory);

        configuration.Keys = supported.Keys
            .Select(CloneKey)
            .ToList();

        var mappingByKeyId = new Dictionary<string, KeyActionMappingConfiguration>(StringComparer.OrdinalIgnoreCase);
        var mappingById = new Dictionary<string, KeyActionMappingConfiguration>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in configuration.Mappings ?? [])
        {
            if (mapping is null)
            {
                continue;
            }

            var keyId = NormalizeOptional(mapping.KeyId);
            if (!string.IsNullOrWhiteSpace(keyId) && !mappingByKeyId.ContainsKey(keyId))
            {
                mappingByKeyId[keyId] = mapping;
            }

            var id = NormalizeOptional(mapping.Id);
            if (!string.IsNullOrWhiteSpace(id) && !mappingById.ContainsKey(id))
            {
                mappingById[id] = mapping;
            }
        }

        configuration.Mappings = supported.Mappings
            .Select(template =>
            {
                mappingByKeyId.TryGetValue(template.KeyId, out var mappingByKey);
                mappingById.TryGetValue(template.Id, out var mappingByTemplateId);
                return NormalizeFixedMapping(mappingByKey ?? mappingByTemplateId, template, baseDirectory);
            })
            .ToList();

        return configuration;
    }

    private static KeyDefinitionConfiguration CloneKey(KeyDefinitionConfiguration key)
    {
        return new KeyDefinitionConfiguration
        {
            Id = NormalizeId(key.Id),
            Name = NormalizeName(key.Name, LocalizedText.Pick("Unnamed key", "未命名按键")),
            Trigger = key.Trigger ?? new EventMatcherConfiguration()
        };
    }

    private static KeyActionMappingConfiguration NormalizeFixedMapping(
        KeyActionMappingConfiguration? mapping,
        KeyActionMappingConfiguration template,
        string? baseDirectory)
    {
        var source = mapping ?? template;
        var forceOsdOnly = SupportedDeviceConfiguration.ShouldUseOsdOnlyDefault(template.KeyId);
        var normalizedAction = NormalizeAction(
            forceOsdOnly
            ? template.Action ?? new ActionDefinitionConfiguration()
            : source.Action ?? template.Action ?? new ActionDefinitionConfiguration());
        var normalizedOsd = NormalizeMappingOsd(source.Osd ?? template.Osd, baseDirectory);
        if (forceOsdOnly)
        {
            normalizedOsd.Enabled = true;
            if (string.IsNullOrWhiteSpace(normalizedOsd.Title))
            {
                normalizedOsd.Title = template.Osd?.Title;
            }

            if (string.IsNullOrWhiteSpace(normalizedOsd.Icon.Path) && !string.IsNullOrWhiteSpace(template.Osd?.Icon?.Path))
            {
                normalizedOsd.Icon = NormalizeIcon(template.Osd.Icon, baseDirectory);
            }
        }

        var hasAssignedAction = !string.IsNullOrWhiteSpace(normalizedAction.Type);

        return new KeyActionMappingConfiguration
        {
            Id = template.Id,
            Name = template.Name,
            Enabled = hasAssignedAction && (mapping?.Enabled ?? template.Enabled),
            KeyId = template.KeyId,
            Action = normalizedAction,
            Osd = normalizedOsd
        };
    }

    private static ActionDefinitionConfiguration NormalizeAction(ActionDefinitionConfiguration? action)
    {
        action ??= new ActionDefinitionConfiguration();
        action.Type = NormalizeActionType(action.Type);
        action.KeyChord = action.Type == HotkeyActionType.SendStandardKey
            ? StandardKeyCatalog.NormalizeChord(action.KeyChord)
            : null;

        var target = NormalizeOptional(action.Target);
        var arguments = NormalizeOptional(action.Arguments);

        action.Target = action.Type == HotkeyActionType.OpenApplication ? target : null;
        action.Arguments = action.Type == HotkeyActionType.OpenApplication ? arguments : null;
        return action;
    }

    private static TouchpadConfiguration NormalizeTouchpadConfiguration(TouchpadConfiguration? touchpad, string? baseDirectory)
    {
        touchpad ??= new TouchpadConfiguration();
        var surfaceWidth = RuntimeDefaults.DefaultTouchpadSurfaceWidth;
        var surfaceHeight = RuntimeDefaults.DefaultTouchpadSurfaceHeight;
        return new TouchpadConfiguration
        {
            Enabled = touchpad.Enabled,
            LightPressThreshold = Math.Clamp(
                touchpad.LightPressThreshold <= 0 ? RuntimeDefaults.DefaultTouchpadLightPressThreshold : touchpad.LightPressThreshold,
                20,
                RuntimeDefaults.DefaultTouchpadDeepPressThreshold - 1),
            DeepPressThreshold = Math.Clamp(
                RuntimeDefaults.DefaultTouchpadDeepPressThreshold,
                RuntimeDefaults.DefaultTouchpadDeepPressThreshold,
                RuntimeDefaults.DefaultTouchpadDeepPressThreshold),
            LongPressDurationMs = Math.Clamp(
                touchpad.LongPressDurationMs <= 0 ? RuntimeDefaults.DefaultTouchpadCornerLongPressDurationMs : touchpad.LongPressDurationMs,
                200,
                3000),
            SurfaceWidth = surfaceWidth,
            SurfaceHeight = surfaceHeight,
            DeepPressAction = NormalizeAction(touchpad.DeepPressAction),
            LeftTopCorner = NormalizeTouchpadCornerRegion(
                touchpad.LeftTopCorner,
                TouchpadCornerRegionConfiguration.CreateLeftTopDefault(),
                surfaceWidth,
                surfaceHeight),
            RightTopCorner = NormalizeTouchpadCornerRegion(
                touchpad.RightTopCorner,
                TouchpadCornerRegionConfiguration.CreateRightTopDefault(),
                surfaceWidth,
                surfaceHeight)
        };
    }

    private static TouchpadCornerRegionConfiguration NormalizeTouchpadCornerRegion(
        TouchpadCornerRegionConfiguration? region,
        TouchpadCornerRegionConfiguration template,
        int surfaceWidth,
        int surfaceHeight)
    {
        var bounds = NormalizeTouchpadBounds(region?.Bounds, template.Bounds, surfaceWidth, surfaceHeight);
        return new TouchpadCornerRegionConfiguration
        {
            Id = template.Id,
            Bounds = bounds,
            DeepPressAction = NormalizeAction(region?.DeepPressAction ?? template.DeepPressAction),
            LongPressAction = NormalizeAction(region?.LongPressAction ?? template.LongPressAction)
        };
    }

    private static TouchpadRegionBoundsConfiguration NormalizeTouchpadBounds(
        TouchpadRegionBoundsConfiguration? bounds,
        TouchpadRegionBoundsConfiguration template,
        int surfaceWidth,
        int surfaceHeight)
    {
        var normalized = new TouchpadRegionBoundsConfiguration
        {
            Left = Math.Clamp(bounds?.Left ?? template.Left, 0, surfaceWidth - 1),
            Top = Math.Clamp(bounds?.Top ?? template.Top, 0, surfaceHeight - 1),
            Right = Math.Clamp(bounds?.Right ?? template.Right, 1, surfaceWidth),
            Bottom = Math.Clamp(bounds?.Bottom ?? template.Bottom, 1, surfaceHeight)
        };

        if (normalized.Right <= normalized.Left || normalized.Bottom <= normalized.Top)
        {
            return new TouchpadRegionBoundsConfiguration
            {
                Left = template.Left,
                Top = template.Top,
                Right = template.Right,
                Bottom = template.Bottom
            };
        }

        return normalized;
    }

    private static MappingOsdConfiguration NormalizeMappingOsd(
        MappingOsdConfiguration? osd,
        string? baseDirectory)
    {
        osd ??= new MappingOsdConfiguration();

        var title = NormalizeOptional(osd.Title);
        if (!string.IsNullOrWhiteSpace(title) && title.Length > RuntimeDefaults.MaxOsdTitleLength)
        {
            title = title[..RuntimeDefaults.MaxOsdTitleLength];
        }

        var icon = NormalizeIcon(osd.Icon, baseDirectory);

        return new MappingOsdConfiguration
        {
            Enabled = osd.Enabled,
            Title = title,
            Icon = icon
        };
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
