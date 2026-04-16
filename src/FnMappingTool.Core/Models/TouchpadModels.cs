namespace FnMappingTool.Core.Models;

public sealed class TouchpadConfiguration
{
    public bool Enabled { get; set; } = true;

    public int DeepPressThreshold { get; set; } = RuntimeDefaults.DefaultTouchpadDeepPressThreshold;

    public ActionDefinitionConfiguration DeepPressAction { get; set; } = new();
}

public sealed class TouchpadLiveStateSnapshot
{
    public bool IsRegistered { get; set; }

    public bool HasReceivedInput { get; set; }

    public bool SupportsPressure { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public bool HasInteraction { get; set; }

    public bool ButtonPressed { get; set; }

    public bool DeepPressed { get; set; }

    public int Pressure { get; set; }

    public int PeakPressure { get; set; }

    public int DeepPressThreshold { get; set; } = RuntimeDefaults.DefaultTouchpadDeepPressThreshold;

    public ushort ScanTime { get; set; }

    public byte ContactCount { get; set; }

    public List<TouchpadLiveContactSnapshot> Contacts { get; set; } = [];
}

public sealed class TouchpadLiveContactSnapshot
{
    public int SlotIndex { get; set; }

    public bool Tip { get; set; }

    public bool Confidence { get; set; }

    public int ContactId { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Pressure { get; set; }
}
