using System.Globalization;
using System.Xml.Linq;

namespace MeowBox.Core.Services;

public static class ResourceStringService
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetString(string key, string fallback)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        var resources = GetFlatResources();
        return resources.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    public static string GetCurrentLanguageTag()
    {
        return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguageService.ChineseTag
            : AppLanguageService.EnglishTag;
    }

    public static IReadOnlyDictionary<string, string> GetFlatResources()
    {
        var languageTag = GetCurrentLanguageTag();

        lock (SyncRoot)
        {
            if (!Cache.TryGetValue(languageTag, out var resources))
            {
                resources = LoadFlatResources(languageTag);
                Cache[languageTag] = resources;
            }

            return resources;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadFlatResources(string languageTag)
    {
        var path = ResolveResourcesPath(languageTag);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            if (!string.Equals(languageTag, AppLanguageService.EnglishTag, StringComparison.OrdinalIgnoreCase))
            {
                return LoadFlatResources(AppLanguageService.EnglishTag);
            }

            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var resources = new Dictionary<string, string>(StringComparer.Ordinal);
        var document = XDocument.Load(path);
        foreach (var data in document.Root?.Elements("data") ?? [])
        {
            var name = data.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            resources[name] = data.Element("value")?.Value ?? string.Empty;
        }

        return resources;
    }

    private static string? ResolveResourcesPath(string languageTag)
    {
        foreach (var baseDirectory in EnumerateBaseDirectories())
        {
            var path = Path.Combine(baseDirectory, "Strings", languageTag, "Resources.resw");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateBaseDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (seen.Add(directory.FullName))
            {
                yield return directory.FullName;
            }
        }
    }
}
