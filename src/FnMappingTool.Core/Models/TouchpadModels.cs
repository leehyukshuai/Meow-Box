namespace FnMappingTool.Core.Models;

public sealed class TouchpadConfiguration
{
    public bool Enabled { get; set; } = true;

    public int DeepPressThreshold { get; set; } = RuntimeDefaults.DefaultTouchpadDeepPressThreshold;

    public int LongPressDurationMs { get; set; } = RuntimeDefaults.DefaultTouchpadCornerLongPressDurationMs;

    public int SurfaceWidth { get; set; } = RuntimeDefaults.DefaultTouchpadSurfaceWidth;

    public int SurfaceHeight { get; set; } = RuntimeDefaults.DefaultTouchpadSurfaceHeight;

    public ActionDefinitionConfiguration DeepPressAction { get; set; } = new();

    public TouchpadCornerRegionConfiguration LeftTopCorner { get; set; } = TouchpadCornerRegionConfiguration.CreateLeftTopDefault();

    public TouchpadCornerRegionConfiguration RightTopCorner { get; set; } = TouchpadCornerRegionConfiguration.CreateRightTopDefault();
}

public sealed class TouchpadCornerRegionConfiguration
{
    public string Id { get; set; } = string.Empty;

    public TouchpadRegionBoundsConfiguration Bounds { get; set; } = new();

    public ActionDefinitionConfiguration DeepPressAction { get; set; } = new();

    public ActionDefinitionConfiguration LongPressAction { get; set; } = new();

    public static TouchpadCornerRegionConfiguration CreateLeftTopDefault()
    {
        return new TouchpadCornerRegionConfiguration
        {
            Id = TouchpadCornerRegionId.LeftTop,
            Bounds = new TouchpadRegionBoundsConfiguration
            {
                Left = 0,
                Top = 0,
                Right = RuntimeDefaults.DefaultTouchpadCornerWidth,
                Bottom = RuntimeDefaults.DefaultTouchpadCornerHeight
            }
        };
    }

    public static TouchpadCornerRegionConfiguration CreateRightTopDefault()
    {
        return new TouchpadCornerRegionConfiguration
        {
            Id = TouchpadCornerRegionId.RightTop,
            Bounds = new TouchpadRegionBoundsConfiguration
            {
                Left = RuntimeDefaults.DefaultTouchpadSurfaceWidth - RuntimeDefaults.DefaultTouchpadCornerWidth,
                Top = 0,
                Right = RuntimeDefaults.DefaultTouchpadSurfaceWidth,
                Bottom = RuntimeDefaults.DefaultTouchpadCornerHeight
            }
        };
    }
}

public sealed class TouchpadRegionBoundsConfiguration
{
    public int Left { get; set; }

    public int Top { get; set; }

    public int Right { get; set; }

    public int Bottom { get; set; }
}

public static class TouchpadCornerRegionId
{
    public const string LeftTop = "left-top";
    public const string RightTop = "right-top";
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
