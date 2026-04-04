namespace FnMappingTool.Worker;

internal sealed class WorkerApplicationContext : ApplicationContext
{
    private readonly WorkerHost _host;

    public WorkerApplicationContext()
    {
        _host = new WorkerHost(ExitThread);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.Dispose();
        }

        base.Dispose(disposing);
    }
}
