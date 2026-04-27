using System.IO.Pipes;
using System.Text.Json;
using MeowBox.Core.Contracts;

namespace MeowBox.Controller.Services;

internal sealed class ControllerPipeServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<WorkerNotification, Task> _handler;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<Task> _connections = [];

    public ControllerPipeServer(Func<WorkerNotification, Task> handler)
    {
        _handler = handler;
        _ = AcceptLoopAsync(_cancellationTokenSource.Token);
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        try
        {
            Task.WaitAll(_connections.ToArray(), TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var server = NamedPipeServerStreamAcl.Create(
                ControllerPipeConstants.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                ControllerPipeSecurityFactory.Create());

            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                var task = HandleConnectionAsync(server, cancellationToken);
                _connections.Add(task);
                _ = task.ContinueWith(_ => _connections.Remove(task), TaskScheduler.Default);
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                server.Dispose();
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        await using var _ = stream.ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };

        var payload = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        var ack = new WorkerNotificationAck { Success = true };
        try
        {
            var notification = JsonSerializer.Deserialize<WorkerNotification>(payload, JsonOptions) ?? new WorkerNotification();
            await _handler(notification);
        }
        catch
        {
            ack.Success = false;
        }

        var response = JsonSerializer.Serialize(ack, JsonOptions);
        await writer.WriteLineAsync(response);
    }
}
