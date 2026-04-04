using System.Threading;

namespace FnMappingTool.Worker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "FnMappingTool.Worker.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        using var context = new WorkerApplicationContext();
        Application.Run(context);
    }
}
