namespace FnMappingTool.Core.Services;

public static class BuiltInAssetResolver
{
    public const string AssetsDirectoryName = "assets";
    public const string AppIconsDirectoryName = "app";
    public const string OsdIconsDirectoryName = "osd";
    public const string PresetsDirectoryName = "presets";

    private static readonly string[] OsdExtensions = [".svg", ".png", ".jpg", ".jpeg", ".bmp", ".ico"];
    private static readonly string[] AppExtensions = [".ico"];

    public static string? ResolveOsdAssetPath(string? assetKey) =>
        ResolveAssetPath(OsdIconsDirectoryName, assetKey, OsdExtensions);

    public static string? ResolveApplicationIconPath(string? assetKey) =>
        ResolveAssetPath(AppIconsDirectoryName, assetKey, AppExtensions);

    public static string GetAssetsPath(params string[] segments)
    {
        foreach (var root in EnumerateAssetRoots())
        {
            var candidate = segments.Aggregate(root, Path.Combine);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return segments.Aggregate(Path.Combine(AppContext.BaseDirectory, AssetsDirectoryName), Path.Combine);
    }

    private static string? ResolveAssetPath(string category, string? assetKey, IReadOnlyList<string> extensions)
    {
        if (string.IsNullOrWhiteSpace(assetKey))
        {
            return null;
        }

        foreach (var root in EnumerateAssetRoots())
        {
            var categoryRoot = Path.Combine(root, category);
            if (!Directory.Exists(categoryRoot))
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(categoryRoot, assetKey + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateAssetRoots()
    {
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateAssetRootCandidates())
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath) && discovered.Add(fullPath))
            {
                yield return fullPath;
            }
        }
    }

    private static IEnumerable<string> EnumerateAssetRootCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, AssetsDirectoryName);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            yield return Path.Combine(directory.FullName, AssetsDirectoryName);
            yield return Path.GetFullPath(Path.Combine(directory.FullName, "..", "..", "..", AssetsDirectoryName));
        }
    }
}
