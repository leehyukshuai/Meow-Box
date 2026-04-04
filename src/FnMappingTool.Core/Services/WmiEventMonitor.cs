using System.Management;

namespace FnMappingTool.Core.Services;

public sealed class InputEvent
{
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public string Source { get; init; } = "Wmi";

    public string WmiClassName { get; init; } = string.Empty;

    public bool? WmiActive { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public string ReportHex { get; init; } = string.Empty;

    public int? VirtualKey { get; init; }

    public int? MakeCode { get; init; }

    public int? Flags { get; init; }
}

public sealed class WmiEventMonitor : IDisposable
{
    private readonly List<ManagementEventWatcher> _watchers = new();
    private readonly Action<InputEvent> _onEvent;
    private readonly Action<string> _onStatus;

    public WmiEventMonitor(Action<InputEvent> onEvent, Action<string> onStatus)
    {
        _onEvent = onEvent;
        _onStatus = onStatus;
    }

    public IReadOnlyList<string> Start()
    {
        var classNames = new[]
        {
            "HID_EVENT20",
            "HID_EVENT21",
            "HID_EVENT22",
            "HID_EVENT23",
            "WMIEvent"
        };

        var started = new List<string>();
        foreach (var className in classNames)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\WMI");
                scope.Connect();

                var watcher = new ManagementEventWatcher(scope, new WqlEventQuery("SELECT * FROM " + className));
                watcher.EventArrived += (_, args) =>
                {
                    try
                    {
                        var inputEvent = Convert(className, args.NewEvent);
                        if (inputEvent is not null)
                        {
                            _onEvent(inputEvent);
                        }
                    }
                    catch (Exception exception)
                    {
                        _onStatus("WMI parse failed for " + className + ": " + exception.Message);
                    }
                };

                watcher.Start();
                _watchers.Add(watcher);
                started.Add(className);
            }
            catch (Exception exception)
            {
                _onStatus("WMI watcher failed for " + className + ": " + exception.Message);
            }
        }

        return started;
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers.ToArray())
        {
            try
            {
                watcher.Stop();
            }
            catch
            {
            }

            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private static InputEvent? Convert(string className, ManagementBaseObject? wmiEvent)
    {
        if (wmiEvent is null)
        {
            return null;
        }

        var reportHex = string.Empty;
        var instanceName = string.Empty;
        bool? active = null;

        foreach (PropertyData property in wmiEvent.Properties)
        {
            if (string.Equals(property.Name, "EventDetail", StringComparison.OrdinalIgnoreCase) &&
                property.Value is byte[] eventDetail)
            {
                reportHex = BitConverter.ToString(eventDetail);
            }
            else if (string.Equals(property.Name, "InstanceName", StringComparison.OrdinalIgnoreCase) &&
                     property.Value is not null)
            {
                instanceName = property.Value.ToString() ?? string.Empty;
            }
            else if (string.Equals(property.Name, "Active", StringComparison.OrdinalIgnoreCase) &&
                     property.Value is not null)
            {
                active = (bool)property.Value;
            }
        }

        return new InputEvent
        {
            Timestamp = DateTime.Now,
            WmiClassName = className,
            WmiActive = active,
            DeviceName = instanceName,
            ReportHex = reportHex
        };
    }
}
