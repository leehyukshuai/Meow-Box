namespace MeowBox.Worker;

internal sealed class WorkerApplicationContext : ApplicationContext
{
    private readonly WorkerHost _host;
    private int _exitHandled;

    public WorkerApplicationContext()
    {
        _host = new WorkerHost(ExitThread);
    }

    protected override void ExitThreadCore()
    {
        if (Interlocked.Exchange(ref _exitHandled, 1) == 0)
        {
            _host.OnApplicationExit();
        }

        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (Interlocked.Exchange(ref _exitHandled, 1) == 0)
            {
                _host.OnApplicationExit();
            }

            _host.Dispose();
        }

        base.Dispose(disposing);
    }
}
