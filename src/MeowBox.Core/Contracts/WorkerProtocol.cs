using MeowBox.Core.Models;
using MeowBox.Core.Services;

namespace MeowBox.Core.Contracts;

public static class WorkerPipeConstants
{
    public const string PipeName = "MeowBox.WorkerPipe";
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
}

public sealed class WorkerRequest
{
    public string Command { get; set; } = WorkerCommandType.GetStatus;
}

public sealed class WorkerResponse
{
    public bool Success { get; set; }

    public string? Error { get; set; }

    public WorkerStatus? Status { get; set; }
}

public sealed class WorkerStatus
{
    public bool IsRunning { get; set; }

    public bool IsListening { get; set; }

    public bool IsTrayIconVisible { get; set; }

    public string LastEventSummary { get; set; } = string.Empty;

    public string ConfigPath { get; set; } = string.Empty;

    public string StateMessage { get; set; } = string.Empty;

    public TouchpadLiveStateSnapshot Touchpad { get; set; } = new();
}
