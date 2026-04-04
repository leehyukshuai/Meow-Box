using System.Diagnostics;

namespace FnMappingTool.Controller.Services;

public sealed class WorkerProcessService
{
    public string WorkerExecutablePath => EnumerateWorkerExecutableCandidates().First();

    public bool IsWorkerInstalled()
    {
        return EnumerateWorkerExecutableCandidates().Any(File.Exists);
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

        var workerExecutablePath = EnumerateWorkerExecutableCandidates().FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(workerExecutablePath))
        {
            return false;
        }

        if (IsWorkerProcessRunning())
        {
            return true;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = workerExecutablePath,
            Arguments = "--headless",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(workerExecutablePath) ?? AppContext.BaseDirectory
        });

        return process is not null;
    }

    private static IEnumerable<string> EnumerateWorkerExecutableCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "runtime", "worker", "FnMappingTool.Worker.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "FnMappingTool.Worker.exe");
    }
}
