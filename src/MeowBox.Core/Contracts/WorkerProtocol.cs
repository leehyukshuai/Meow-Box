using MeowBox.Core.Models;

namespace MeowBox.Core.Contracts;

public static class WorkerPipeConstants
{
    public const string PipeName = "MeowBox.WorkerPipe";
}

public static class ControllerPipeConstants
{
    public const string PipeName = "MeowBox.ControllerPipe";
}

public static class TouchpadPipeConstants
{
    public const string PipeName = "MeowBox.TouchpadStream";
}

public static class WorkerCommandType
{
    public const string GetStatus = "GetStatus";
    public const string StopWorker = "StopWorker";
    public const string ReloadConfig = "ReloadConfig";
    public const string GetBatteryControlState = "GetBatteryControlState";
    public const string SetPerformanceMode = "SetPerformanceMode";
    public const string SetChargeLimit = "SetChargeLimit";
    public const string AnnounceState = "AnnounceState";
}

public static class WorkerNotificationType
{
    public const string Started = "Started";
    public const string Stopped = "Stopped";
}

public sealed class WorkerRequest
{
    public string Command { get; set; } = WorkerCommandType.GetStatus;

    public string? PerformanceModeKey { get; set; }

    public int? ChargeLimitPercent { get; set; }
}

public sealed class WorkerResponse
{
    public bool Success { get; set; }

    public string? Error { get; set; }

    public WorkerStatus? Status { get; set; }

    public BatteryControlState? Battery { get; set; }
}

public sealed class WorkerStatus
{
    public bool IsRunning { get; set; }

    public bool IsElevated { get; set; }

    public bool IsListening { get; set; }

    public bool IsTrayIconVisible { get; set; }

    public string LastEventSummary { get; set; } = string.Empty;

    public string ConfigPath { get; set; } = string.Empty;

    public string StateMessage { get; set; } = string.Empty;

    public BatteryControlState? Battery { get; set; }

    public TouchpadLiveStateSnapshot Touchpad { get; set; } = new();
}

public sealed class WorkerNotification
{
    public string Type { get; set; } = WorkerNotificationType.Started;

    public WorkerStatus? Status { get; set; }
}

public sealed class WorkerNotificationAck
{
    public bool Success { get; set; }
}
