using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace MeowBox.Core.Services;

public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "MeowBoxWorker";
    private const string StartupShortcutFileName = "Meow Box.lnk";
    private const string ScheduledTaskName = "MeowBoxWorker";
    private const string ElevatedRuntimeTaskName = "MeowBoxWorker.Runtime";
    private const string HeadlessArguments = "--headless";
    private static readonly TimeSpan StartupModeCacheDuration = TimeSpan.FromSeconds(10);

    private readonly object _startupModeCacheLock = new();
    private StartupRegistrationMode? _cachedStartupMode;
    private DateTime _cachedStartupModeUtc;

    public bool IsEnabled()
    {
        return GetStartupMode() != StartupRegistrationMode.Disabled;
    }

    public bool IsPriorityEnabled()
    {
        return GetStartupMode() is StartupRegistrationMode.ScheduledTask or StartupRegistrationMode.RunKey;
    }

    public bool HasMatchingPriorityStartupTask(string workerExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(workerExecutablePath))
        {
            return false;
        }

        var fullWorkerExecutablePath = Path.GetFullPath(workerExecutablePath);
        return TryGetScheduledTaskDefinition(ScheduledTaskName, out var taskDefinition) &&
               string.Equals(taskDefinition.Command, fullWorkerExecutablePath, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(taskDefinition.Arguments?.Trim(), HeadlessArguments, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled, string workerExecutablePath, bool preferPriorityStartup)
    {
        if (!enabled)
        {
            var startupMode = GetStartupMode();
            DeleteStartupShortcut();
            if (startupMode == StartupRegistrationMode.ScheduledTask)
            {
                DeleteScheduledTask();
            }

            RemoveRunEntry();
            SetCachedStartupMode(StartupRegistrationMode.Disabled);
            return;
        }

        if (string.IsNullOrWhiteSpace(workerExecutablePath))
        {
            throw new ArgumentException("The worker executable path cannot be empty.", nameof(workerExecutablePath));
        }

        var fullWorkerExecutablePath = Path.GetFullPath(workerExecutablePath);
        if (!File.Exists(fullWorkerExecutablePath))
        {
            throw new FileNotFoundException("The worker executable could not be found.", fullWorkerExecutablePath);
        }

        if (preferPriorityStartup)
        {
            CreateScheduledTask(fullWorkerExecutablePath);
            DeleteStartupShortcut();
            RemoveRunEntry();
            SetCachedStartupMode(StartupRegistrationMode.ScheduledTask);
            return;
        }

        var existingStartupMode = GetStartupMode();
        CreateStartupShortcut(fullWorkerExecutablePath);
        if (existingStartupMode == StartupRegistrationMode.ScheduledTask)
        {
            DeleteScheduledTask();
        }

        RemoveRunEntry();
        SetCachedStartupMode(StartupRegistrationMode.StartupShortcut);
    }

    public StartupRegistrationMode GetStartupMode()
    {
        if (TryGetCachedStartupMode(out var cachedStartupMode))
        {
            return cachedStartupMode;
        }

        var startupMode = DetectStartupMode();
        SetCachedStartupMode(startupMode);
        return startupMode;
    }

    public bool HasElevatedRuntimeTask()
    {
        return TryGetScheduledTaskDefinition(ElevatedRuntimeTaskName, out _);
    }

    public void EnsureElevatedRuntimeTask(string workerExecutablePath)
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

        if (TryGetScheduledTaskDefinition(ElevatedRuntimeTaskName, out var existingTask) &&
            string.Equals(existingTask.Command, fullWorkerExecutablePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existingTask.Arguments?.Trim(), HeadlessArguments, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CreateElevatedRuntimeTask(fullWorkerExecutablePath);
    }

    public void StartElevatedRuntimeTask()
    {
        var script = string.Join(Environment.NewLine,
        [
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8",
            "Import-Module ScheduledTasks",
            "Start-ScheduledTask -TaskName " + ToPowerShellLiteral(ElevatedRuntimeTaskName) + " -ErrorAction Stop"
        ]);

        var result = RunPowerShellHidden(script);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildToolErrorMessage("start the elevated runtime task", result));
        }
    }

    public void StopElevatedRuntimeTask()
    {
        StopScheduledTask(ElevatedRuntimeTaskName, "stop the elevated runtime task");
    }

    public void StopStartupTask()
    {
        StopScheduledTask(ScheduledTaskName, "stop the startup scheduled task");
    }

    public void BeginDisableSilently()
    {
        var shortcutPathLiteral = ToPowerShellLiteral(StartupShortcutPath);
        var runKeyPathLiteral = ToPowerShellLiteral(RunKeyPath);
        var entryNameLiteral = ToPowerShellLiteral(EntryName);
        var scheduledTaskNameLiteral = ToPowerShellLiteral(ScheduledTaskName);

        var script = $@"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Start-Sleep -Milliseconds 1500
if (Get-Process -Name 'MeowBox.Worker' -ErrorAction SilentlyContinue | Select-Object -First 1)
{{
    exit 0
}}
try
{{
    if (Test-Path -LiteralPath {shortcutPathLiteral})
    {{
        Remove-Item -LiteralPath {shortcutPathLiteral} -Force -ErrorAction SilentlyContinue
    }}
}}
catch
{{
}}
try
{{
    $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey({runKeyPathLiteral}, $true)
    if ($null -ne $runKey)
    {{
        $runKey.DeleteValue({entryNameLiteral}, $false)
        $runKey.Dispose()
    }}
}}
catch
{{
}}
try
{{
    Import-Module ScheduledTasks
    $task = Get-ScheduledTask -TaskName {scheduledTaskNameLiteral} -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $task)
    {{
        Unregister-ScheduledTask -TaskName {scheduledTaskNameLiteral} -Confirm:$false -ErrorAction SilentlyContinue
    }}
}}
catch
{{
}}";

        StartDetachedPowerShell(script);
        SetCachedStartupMode(StartupRegistrationMode.Disabled);
    }

    private static void StopScheduledTask(string taskName, string action)
    {
        var script = string.Join(Environment.NewLine,
        [
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8",
            "Import-Module ScheduledTasks",
            "$task = Get-ScheduledTask -TaskName " + ToPowerShellLiteral(taskName) + " -ErrorAction SilentlyContinue | Select-Object -First 1",
            "if ($null -ne $task)",
            "{",
            "    Stop-ScheduledTask -TaskName " + ToPowerShellLiteral(taskName) + " -ErrorAction SilentlyContinue",
            "}"
        ]);

        RunScheduledTaskCommand(script, action);
    }

    private static string StartupShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutFileName);


    private StartupRegistrationMode DetectStartupMode()
    {
        if (HasValidScheduledTask())
        {
            return StartupRegistrationMode.ScheduledTask;
        }

        if (HasValidStartupShortcut())
        {
            return StartupRegistrationMode.StartupShortcut;
        }

        return HasValidRunEntry()
            ? StartupRegistrationMode.RunKey
            : StartupRegistrationMode.Disabled;
    }

    private bool TryGetCachedStartupMode(out StartupRegistrationMode startupMode)
    {
        lock (_startupModeCacheLock)
        {
            if (_cachedStartupMode.HasValue && DateTime.UtcNow - _cachedStartupModeUtc <= StartupModeCacheDuration)
            {
                startupMode = _cachedStartupMode.Value;
                return true;
            }
        }

        startupMode = StartupRegistrationMode.Disabled;
        return false;
    }

    private void SetCachedStartupMode(StartupRegistrationMode startupMode)
    {
        lock (_startupModeCacheLock)
        {
            _cachedStartupMode = startupMode;
            _cachedStartupModeUtc = DateTime.UtcNow;
        }
    }

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

    private static bool HasValidRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(EntryName) is string command &&
               !string.IsNullOrWhiteSpace(command) &&
               CommandContainsExistingPath(command);
    }

    private static bool HasValidScheduledTask()
    {
        return TryGetScheduledTaskDefinition(ScheduledTaskName, out var taskDefinition) &&
               !string.IsNullOrWhiteSpace(taskDefinition.Command) &&
               File.Exists(taskDefinition.Command) &&
               string.Equals(taskDefinition.Arguments?.Trim(), HeadlessArguments, StringComparison.OrdinalIgnoreCase);
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
            shellLink.SetArguments(HeadlessArguments);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(workerExecutablePath) ?? AppContext.BaseDirectory);
            shellLink.SetDescription("Starts the Meow Box background service.");
            shellLink.SetIconLocation(workerExecutablePath, 0);

            ((IPersistFile)shellLink).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static void CreateScheduledTask(string workerExecutablePath)
    {
        var workingDirectory = Path.GetDirectoryName(workerExecutablePath) ?? AppContext.BaseDirectory;
        var workerPathLiteral = ToPowerShellLiteral(workerExecutablePath);
        var workingDirectoryLiteral = ToPowerShellLiteral(workingDirectory);
        var taskNameLiteral = ToPowerShellLiteral(ScheduledTaskName);
        var descriptionLiteral = ToPowerShellLiteral("Starts the Meow Box background service.");

        var script = $"""
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Import-Module ScheduledTasks
$taskName = {taskNameLiteral}
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Highest
$action = New-ScheduledTaskAction -Execute {workerPathLiteral} -Argument {ToPowerShellLiteral(HeadlessArguments)} -WorkingDirectory {workingDirectoryLiteral}
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -StartWhenAvailable
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description {descriptionLiteral} -Force | Out-Null
""";

        RunScheduledTaskCommand(script, "create the startup scheduled task");
    }

    private static void CreateElevatedRuntimeTask(string workerExecutablePath)
    {
        var workingDirectory = Path.GetDirectoryName(workerExecutablePath) ?? AppContext.BaseDirectory;
        var workerPathLiteral = ToPowerShellLiteral(workerExecutablePath);
        var workingDirectoryLiteral = ToPowerShellLiteral(workingDirectory);
        var taskNameLiteral = ToPowerShellLiteral(ElevatedRuntimeTaskName);
        var descriptionLiteral = ToPowerShellLiteral("Runs the Meow Box background service with administrator privileges on demand.");

        var script = $"""
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Import-Module ScheduledTasks
$taskName = {taskNameLiteral}
$trigger = New-ScheduledTaskTrigger -Once -At ((Get-Date).Date.AddYears(20))
$action = New-ScheduledTaskAction -Execute {workerPathLiteral} -Argument {ToPowerShellLiteral(HeadlessArguments)} -WorkingDirectory {workingDirectoryLiteral}
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -StartWhenAvailable
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description {descriptionLiteral} -Force | Out-Null
""";

        RunScheduledTaskCommand(script, "create the elevated runtime task");
    }

    private static void DeleteStartupShortcut()
    {
        var shortcutPath = StartupShortcutPath;
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private static void DeleteScheduledTask()
    {
        var script = string.Join(Environment.NewLine,
        [
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8",
            "Import-Module ScheduledTasks",
            "$task = Get-ScheduledTask -TaskName " + ToPowerShellLiteral(ScheduledTaskName) + " -ErrorAction SilentlyContinue | Select-Object -First 1",
            "if ($null -ne $task)",
            "{",
            "    Unregister-ScheduledTask -TaskName " + ToPowerShellLiteral(ScheduledTaskName) + " -Confirm:$false -ErrorAction Stop",
            "}"
        ]);

        RunScheduledTaskCommand(script, "remove the startup scheduled task");
    }

    private static void RunScheduledTaskCommand(string script, string action)
    {
        var result = RunPowerShellHidden(script);
        if (result.ExitCode == 0)
        {
            return;
        }

        if (!RequiresElevation(result))
        {
            throw new InvalidOperationException(BuildToolErrorMessage(action, result));
        }

        result = RunPowerShellElevated(script);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildToolErrorMessage(action, result));
        }
    }

    private static bool RequiresElevation(ProcessResult result)
    {
        if (result.ExitCode == 0)
        {
            return false;
        }

        var details = string.Join("\n", new[] { result.StandardError, result.StandardOutput }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(details))
        {
            return false;
        }

        return details.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("0x80070005", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("需要提升", StringComparison.OrdinalIgnoreCase);
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

    private static bool TryGetScheduledTaskDefinition(string taskName, out ScheduledTaskDefinition taskDefinition)
    {
        var script = string.Join(Environment.NewLine,
        [
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8",
            "Import-Module ScheduledTasks",
            "$task = Get-ScheduledTask -TaskName " + ToPowerShellLiteral(taskName) + " -ErrorAction SilentlyContinue | Select-Object -First 1",
            "if ($null -eq $task)",
            "{",
            "    [PSCustomObject]@{ Exists = $false } | ConvertTo-Json -Compress",
            "    exit 0",
            "}",
            "$action = $task.Actions | Select-Object -First 1",
            "[PSCustomObject]@{",
            "    Exists = $true",
            "    Command = $action.Execute",
            "    Arguments = $action.Arguments",
            "    WorkingDirectory = $action.WorkingDirectory",
            "} | ConvertTo-Json -Compress"
        ]);

        var result = RunPowerShell(script);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            taskDefinition = default;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            if (!root.TryGetProperty("Exists", out var existsProperty) || !existsProperty.GetBoolean())
            {
                taskDefinition = default;
                return false;
            }

            taskDefinition = new ScheduledTaskDefinition(
                root.TryGetProperty("Command", out var commandProperty) ? commandProperty.GetString() : null,
                root.TryGetProperty("Arguments", out var argumentsProperty) ? argumentsProperty.GetString() : null,
                root.TryGetProperty("WorkingDirectory", out var workingDirectoryProperty) ? workingDirectoryProperty.GetString() : null);
            return true;
        }
        catch
        {
            taskDefinition = default;
            return false;
        }
    }

    private static void RemoveRunEntry()
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

    private static ProcessResult RunPowerShell(string script, bool requireElevation = false)
    {
        return requireElevation
            ? RunPowerShellElevated(script)
            : RunPowerShellHidden(script);
    }

    private static ProcessResult RunPowerShellHidden(string script)
    {
        var wrappedScript = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" + script;
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrappedScript));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd().Trim();
        var standardError = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static ProcessResult RunPowerShellElevated(string script)
    {
        var errorFilePath = Path.GetTempFileName();
        try
        {
            var wrappedScript = string.Join(Environment.NewLine,
            [
                "try",
                "{",
                script,
                "    exit 0",
                "}",
                "catch",
                "{",
                "    [System.IO.File]::WriteAllText(" + ToPowerShellLiteral(errorFilePath) + ", ($_ | Out-String), [System.Text.Encoding]::UTF8)",
                "    exit 1",
                "}"
            ]);
            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrappedScript));
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encodedCommand}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (process is null)
            {
                return new ProcessResult(1, string.Empty, "Failed to start the elevated PowerShell process.");
            }

            process.WaitForExit();
            var standardError = File.Exists(errorFilePath)
                ? File.ReadAllText(errorFilePath, Encoding.UTF8).Trim()
                : string.Empty;
            return new ProcessResult(process.ExitCode, string.Empty, standardError);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new ProcessResult(1223, string.Empty, "Administrator permission was canceled.");
        }
        finally
        {
            try
            {
                File.Delete(errorFilePath);
            }
            catch
            {
            }
        }
    }

    private static void StartDetachedPowerShell(string script)
    {
        var wrappedScript = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" + script;
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrappedScript));

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encodedCommand}",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static string ToPowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

    private static string BuildToolErrorMessage(string action, ProcessResult result)
    {
        var details = string.Join(Environment.NewLine, new[] { result.StandardError, result.StandardOutput }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(details)
            ? $"Could not {action}."
            : $"Could not {action}. {details}";
    }

    private readonly record struct ScheduledTaskDefinition(string? Command, string? Arguments, string? WorkingDirectory);

    private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);

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

public enum StartupRegistrationMode
{
    Disabled,
    StartupShortcut,
    ScheduledTask,
    RunKey
}
