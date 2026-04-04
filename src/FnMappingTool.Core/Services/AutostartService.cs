using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FnMappingTool.Core.Services;

public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "FnMappingToolWorker";
    private const string StartupShortcutFileName = "Fn Mapping Tool.lnk";

    public bool IsEnabled()
    {
        if (HasValidStartupShortcut())
        {
            return true;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(EntryName) is string command &&
               !string.IsNullOrWhiteSpace(command) &&
               CommandContainsExistingPath(command);
    }

    public void SetEnabled(bool enabled, string workerExecutablePath)
    {
        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(workerExecutablePath))
            {
                throw new ArgumentException("The worker executable path cannot be empty.", nameof(workerExecutablePath));
            }

            var fullWorkerExecutablePath = Path.GetFullPath(workerExecutablePath);
            if (!File.Exists(fullWorkerExecutablePath))
            {
                throw new FileNotFoundException("The worker executable could not be found.", fullWorkerExecutablePath);
            }

            try
            {
                CreateStartupShortcut(fullWorkerExecutablePath);
                RemoveLegacyRunEntry();
                return;
            }
            catch
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
                key?.SetValue(EntryName, Quote(fullWorkerExecutablePath) + " --headless");
                return;
            }
        }

        DeleteStartupShortcut();
        RemoveLegacyRunEntry();
    }

    private static string StartupShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutFileName);

    private static bool HasValidStartupShortcut()
    {
        var shortcutPath = StartupShortcutPath;
        if (!File.Exists(shortcutPath))
        {
            return false;
        }

        return TryResolveShortcutTarget(shortcutPath, out var targetPath) &&
               !string.IsNullOrWhiteSpace(targetPath) &&
               File.Exists(targetPath);
    }

    private static void CreateStartupShortcut(string workerExecutablePath)
    {
        var shortcutPath = StartupShortcutPath;
        var startupDirectory = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrWhiteSpace(startupDirectory))
        {
            Directory.CreateDirectory(startupDirectory);
        }

        var shellLink = (IShellLinkW)new ShellLink();
        try
        {
            shellLink.SetPath(workerExecutablePath);
            shellLink.SetArguments("--headless");
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(workerExecutablePath) ?? AppContext.BaseDirectory);
            shellLink.SetDescription("Starts the Fn Mapping Tool background service.");
            shellLink.SetIconLocation(workerExecutablePath, 0);

            ((IPersistFile)shellLink).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static void DeleteStartupShortcut()
    {
        var shortcutPath = StartupShortcutPath;
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private static bool TryResolveShortcutTarget(string shortcutPath, out string? targetPath)
    {
        targetPath = null;

        var shellLink = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)shellLink).Load(shortcutPath, 0);

            var builder = new StringBuilder(260);
            shellLink.GetPath(builder, builder.Capacity, IntPtr.Zero, 0);
            targetPath = builder.ToString();
            return !string.IsNullOrWhiteSpace(targetPath);
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static void RemoveLegacyRunEntry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        key?.DeleteValue(EntryName, false);
    }

    private static bool CommandContainsExistingPath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        string candidatePath;
        if (command[0] == '"')
        {
            var closingQuoteIndex = command.IndexOf('"', 1);
            candidatePath = closingQuoteIndex > 1
                ? command[1..closingQuoteIndex]
                : string.Empty;
        }
        else
        {
            var firstSpaceIndex = command.IndexOf(' ');
            candidatePath = firstSpaceIndex > 0
                ? command[..firstSpaceIndex]
                : command;
        }

        return !string.IsNullOrWhiteSpace(candidatePath) && File.Exists(candidatePath);
    }

    private static string Quote(string path)
    {
        return "\"" + path + "\"";
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
