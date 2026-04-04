using Microsoft.Win32;

namespace FnMappingTool.Core.Services;

public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "FnMappingToolWorker";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(EntryName) is string command && !string.IsNullOrWhiteSpace(command);
    }

    public void SetEnabled(bool enabled, string workerExecutablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (enabled)
        {
            key?.SetValue(EntryName, Quote(workerExecutablePath) + " --headless");
        }
        else
        {
            key?.DeleteValue(EntryName, false);
        }
    }

    private static string Quote(string path)
    {
        return "\"" + path + "\"";
    }
}
