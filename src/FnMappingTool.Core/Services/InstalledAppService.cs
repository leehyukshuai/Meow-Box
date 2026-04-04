using System.Diagnostics;
using System.Text.Json;

namespace FnMappingTool.Core.Services;

public sealed class InstalledAppEntry
{
    public string Name { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string LaunchTarget => @"shell:AppsFolder\" + AppId;
}

public sealed class InstalledAppService
{
    private IReadOnlyList<InstalledAppEntry>? _cache;

    public async Task<IReadOnlyList<InstalledAppEntry>> GetInstalledAppsAsync()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command \"Get-StartApps | Sort-Object Name | Select-Object Name,AppID | ConvertTo-Json -Depth 2\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return Array.Empty<InstalledAppEntry>();
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<InstalledAppEntry>();
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            _cache = Parse(document.RootElement)
                .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.AppId))
                .DistinctBy(item => item.AppId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return _cache;
        }
        catch
        {
            return Array.Empty<InstalledAppEntry>();
        }
    }

    private static IEnumerable<InstalledAppEntry> Parse(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray().SelectMany(Parse),
            JsonValueKind.Object => TryParseEntry(element),
            _ => Array.Empty<InstalledAppEntry>()
        };
    }

    private static IEnumerable<InstalledAppEntry> TryParseEntry(JsonElement element)
    {
        var name = element.TryGetProperty("Name", out var nameValue) ? nameValue.GetString() : null;
        var appId = element.TryGetProperty("AppID", out var appIdValue) ? appIdValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId))
        {
            return Array.Empty<InstalledAppEntry>();
        }

        return
        [
            new InstalledAppEntry
            {
                Name = name.Trim(),
                AppId = appId.Trim()
            }
        ];
    }
}
