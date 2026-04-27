using System.Diagnostics;
using MeowBox.Core.Services;

namespace MeowBox.Controller.Services;

public sealed class WorkerProcessService
{
    private readonly AutostartService _autostartService = new();
    private readonly object _workerPathSync = new();
    private string? _cachedWorkerExecutablePath;
    private bool _elevatedRuntimeTaskKnownAvailable;

    public string WorkerExecutablePath => ResolveWorkerExecutablePath() ?? EnumerateWorkerExecutableCandidates().First();

    public bool IsWorkerInstalled()
    {
        return ResolveWorkerExecutablePath() is not null || HasElevatedRuntimeTask();
    }

    public bool IsWorkerProcessRunning()
    {
        return Process.GetProcessesByName("MeowBox.Worker").Length > 0;
    }

    public bool HasElevatedRuntimeTask()
    {
        if (_elevatedRuntimeTaskKnownAvailable)
        {
            return true;
        }

        var hasTask = _autostartService.HasElevatedRuntimeTask();
        if (hasTask)
        {
            _elevatedRuntimeTaskKnownAvailable = true;
        }

        return hasTask;
    }

    public void EnsureElevatedRuntimeTask()
    {
        var workerExecutablePath = ResolveWorkerExecutablePath();
        if (string.IsNullOrWhiteSpace(workerExecutablePath))
        {
            throw new InvalidOperationException("Worker executable was not found.");
        }

        _autostartService.EnsureElevatedRuntimeTask(workerExecutablePath);
        _elevatedRuntimeTaskKnownAvailable = true;
    }

    public bool StartWorker()
    {
        if (IsWorkerProcessRunning())
        {
            return true;
        }

        EnsureElevatedRuntimeTask();
        _autostartService.StartElevatedRuntimeTask();
        return true;
    }

    public void StopWorker()
    {
        _autostartService.StopElevatedRuntimeTask();
        _autostartService.StopStartupTask();
    }

    public string? ResolveWorkerExecutablePath()
    {
        lock (_workerPathSync)
        {
            if (!string.IsNullOrWhiteSpace(_cachedWorkerExecutablePath) && File.Exists(_cachedWorkerExecutablePath))
            {
                return _cachedWorkerExecutablePath;
            }
        }

        var resolvedPath = EnumerateWorkerExecutableCandidates()
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return null;
        }

        lock (_workerPathSync)
        {
            _cachedWorkerExecutablePath = resolvedPath;
        }

        return resolvedPath;
    }

    private static IEnumerable<string> EnumerateWorkerExecutableCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateDirectWorkerExecutableCandidates())
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        foreach (var directory in EnumerateSearchRoots())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            IEnumerable<string> matches;
            try
            {
                matches = Directory.EnumerateFiles(directory, "MeowBox.Worker.exe", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var match in matches)
            {
                if (seen.Add(match))
                {
                    yield return match;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectWorkerExecutableCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "runtime", "worker", "MeowBox.Worker.exe");
        yield return Path.Combine(baseDirectory, "MeowBox.Worker.exe");

        var current = new DirectoryInfo(baseDirectory);
        for (var depth = 0; depth < 6 && current is not null; depth++, current = current.Parent)
        {
            yield return Path.Combine(current.FullName, "build", "bin", "MeowBox.Worker", "net8.0-windows10.0.19041.0", "MeowBox.Worker.exe");
            yield return Path.Combine(current.FullName, "build", "bin", "MeowBox.Worker", "net8.0-windows10.0.19041.0", "win-x64", "MeowBox.Worker.exe");
            yield return Path.Combine(current.FullName, "artifacts", "MeowBox", "runtime", "worker", "MeowBox.Worker.exe");
            yield return Path.Combine(current.FullName, "build", "publish", "worker", "MeowBox.Worker.exe");
            yield return Path.Combine(current.FullName, "build", "package", "MeowBox", "runtime", "worker", "MeowBox.Worker.exe");
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && current is not null; depth++, current = current.Parent)
        {
            yield return Path.Combine(current.FullName, "build", "bin", "MeowBox.Worker");
            yield return Path.Combine(current.FullName, "artifacts", "MeowBox", "runtime", "worker");
            yield return Path.Combine(current.FullName, "build", "publish", "worker");
            yield return Path.Combine(current.FullName, "build", "package", "MeowBox", "runtime", "worker");
        }
    }
}
