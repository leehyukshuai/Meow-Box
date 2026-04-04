using System.Diagnostics;

namespace FnMappingTool.Controller.Services;

public sealed class WorkerProcessService
{
    public string WorkerExecutablePath =>
        Path.Combine(AppContext.BaseDirectory, "FnMappingTool.Worker.exe");

    public bool IsWorkerInstalled()
    {
        return File.Exists(WorkerExecutablePath);
    }

    public bool IsWorkerProcessRunning()
    {
        return Process.GetProcessesByName("FnMappingTool.Worker").Length > 0;
    }

    public bool StartWorker()
    {
        if (!IsWorkerInstalled())
        {
            return false;
        }

        if (IsWorkerProcessRunning())
        {
            return true;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = WorkerExecutablePath,
            Arguments = "--headless",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(WorkerExecutablePath) ?? AppContext.BaseDirectory
        });

        return process is not null;
    }
}
