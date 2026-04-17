using System.Diagnostics;

namespace MeowBox.Controller.Services;

public sealed class WorkerProcessService
{
    public string WorkerExecutablePath => ResolveWorkerExecutablePath() ?? EnumerateWorkerExecutableCandidates().First();

    public bool IsWorkerInstalled()
    {
        return ResolveWorkerExecutablePath() is not null;
    }

    public bool IsWorkerProcessRunning()
    {
        return Process.GetProcessesByName("MeowBox.Worker").Length > 0;
    }

    public bool StartWorker()
    {
        if (!IsWorkerInstalled())
        {
            return false;
        }

        var workerExecutablePath = ResolveWorkerExecutablePath();
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

    public string? ResolveWorkerExecutablePath()
    {
        return EnumerateWorkerExecutableCandidates().FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> EnumerateWorkerExecutableCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "runtime", "worker", "MeowBox.Worker.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "MeowBox.Worker.exe");
    }
}
