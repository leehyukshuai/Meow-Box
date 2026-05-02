using System.Threading;
using MeowBox.Core.Services;

namespace MeowBox.Worker;

internal static class Program
{
    private const string HeadlessArgument = "--headless";

    [STAThread]
    private static void Main(string[] args)
    {
        if (!UnelevatedProcessLauncher.IsCurrentProcessElevated())
        {
            if (!IsHeadlessLaunch(args))
            {
                MessageBox.Show(
                    "MeowBox Worker must be started by MeowBox Controller.",
                    "MeowBox Worker",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            Environment.ExitCode = 1;
            return;
        }

        using var mutex = new Mutex(true, "MeowBox.Worker.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        using var context = new WorkerApplicationContext();
        Application.Run(context);
    }

    private static bool IsHeadlessLaunch(string[] args)
    {
        return args.Any(argument => string.Equals(argument, HeadlessArgument, StringComparison.OrdinalIgnoreCase));
    }
}
