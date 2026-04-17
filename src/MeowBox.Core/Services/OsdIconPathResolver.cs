namespace MeowBox.Core.Services;

public static class OsdIconPathResolver
{
    public const string OsdIconDirectoryName = "osd-icons";

    public static string GetOsdIconDirectory(string configDirectory)
    {
        return Path.Combine(configDirectory, OsdIconDirectoryName);
    }

    public static string? NormalizeConfigPath(string? path, string? baseDirectory)
    {
        var normalized = NormalizeOptional(path);
        if (string.IsNullOrWhiteSpace(normalized) || !HasPngExtension(normalized))
        {
            return null;
        }

        if (!Path.IsPathRooted(normalized))
        {
            var relativePath = SanitizeRelativePath(normalized);
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                return relativePath;
            }

            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                var resolved = Path.GetFullPath(Path.Combine(baseDirectory, normalized));
                return NormalizeAbsolutePath(resolved, baseDirectory);
            }

            return SanitizeRelativePath(Path.GetFileName(normalized));
        }

        return NormalizeAbsolutePath(normalized, baseDirectory);
    }

    public static string? ResolveAbsolutePath(string? configPath, string configDirectory)
    {
        var relativePath = NormalizeConfigPath(configPath, configDirectory);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        return Path.Combine(GetOsdIconDirectory(configDirectory), relativePath);
    }

    private static string? NormalizeAbsolutePath(string absolutePath, string? baseDirectory)
    {
        if (!HasPngExtension(absolutePath))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            var iconDirectory = GetOsdIconDirectory(baseDirectory);
            var fullIconDirectory = Path.GetFullPath(iconDirectory);
            var fullPath = Path.GetFullPath(absolutePath);
            var relativePath = Path.GetRelativePath(fullIconDirectory, fullPath);
            var sanitizedRelativePath = SanitizeRelativePath(relativePath);
            if (!string.IsNullOrWhiteSpace(sanitizedRelativePath))
            {
                var resolvedRoundTripPath = Path.GetFullPath(Path.Combine(fullIconDirectory, sanitizedRelativePath));
                if (string.Equals(resolvedRoundTripPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return sanitizedRelativePath;
                }
            }
        }

        return SanitizeRelativePath(Path.GetFileName(absolutePath));
    }

    private static bool HasPngExtension(string path)
    {
        return string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SanitizeRelativePath(string? path)
    {
        var normalized = NormalizeOptional(path);
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized))
        {
            return null;
        }

        var segments = normalized
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0 || segments.Any(static segment => segment is "." or ".."))
        {
            return null;
        }

        var relativePath = Path.Combine(segments);
        return HasPngExtension(relativePath) ? relativePath : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
