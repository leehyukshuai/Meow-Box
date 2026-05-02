using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MeowBox.Core.Services;

public static class UnelevatedProcessLauncher
{
    private const int DesktopFolderId = 0;
    private const int DesktopShellWindow = 8;
    private const int NeedDispatch = 1;

    public static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool TryStart(string executablePath, string? workingDirectory, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            errorMessage = "Executable path was not provided.";
            return false;
        }

        object? shellApplication = null;
        object? shellWindows = null;
        object? desktopDispatch = null;
        object? desktopDocument = null;
        object? desktopApplication = null;

        try
        {
            var shellApplicationType = Type.GetTypeFromProgID("Shell.Application");
            if (shellApplicationType is null)
            {
                errorMessage = "Windows shell is unavailable.";
                return false;
            }

            shellApplication = Activator.CreateInstance(shellApplicationType);
            if (shellApplication is null)
            {
                errorMessage = "Windows shell is unavailable.";
                return false;
            }

            shellWindows = shellApplicationType.InvokeMember(
                "Windows",
                BindingFlags.InvokeMethod,
                null,
                shellApplication,
                null);
            if (shellWindows is null)
            {
                errorMessage = "Windows shell window collection is unavailable.";
                return false;
            }

            object desktopLocation = DesktopFolderId;
            object empty = Type.Missing;
            desktopDispatch = shellWindows.GetType().InvokeMember(
                "FindWindowSW",
                BindingFlags.InvokeMethod,
                null,
                shellWindows,
                [desktopLocation, empty, DesktopShellWindow, 0, NeedDispatch]);
            if (desktopDispatch is null)
            {
                errorMessage = "Windows desktop shell was not found.";
                return false;
            }

            desktopDocument = desktopDispatch.GetType().InvokeMember(
                "Document",
                BindingFlags.GetProperty,
                null,
                desktopDispatch,
                null);
            if (desktopDocument is null)
            {
                errorMessage = "Windows desktop document is unavailable.";
                return false;
            }

            desktopApplication = desktopDocument.GetType().InvokeMember(
                "Application",
                BindingFlags.GetProperty,
                null,
                desktopDocument,
                null);
            if (desktopApplication is null)
            {
                errorMessage = "Windows desktop application shell is unavailable.";
                return false;
            }

            desktopApplication.GetType().InvokeMember(
                "ShellExecute",
                BindingFlags.InvokeMethod,
                null,
                desktopApplication,
                [executablePath, null, workingDirectory, "open", 1]);

            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = "Failed to start process through Windows shell: " + exception.Message;
            return false;
        }
        finally
        {
            ReleaseComObject(desktopApplication);
            ReleaseComObject(desktopDocument);
            ReleaseComObject(desktopDispatch);
            ReleaseComObject(shellWindows);
            ReleaseComObject(shellApplication);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
