using System.IO.Pipes;
using System.Text.Json;
using MeowBox.Core.Contracts;

namespace MeowBox.Worker.Services;

internal sealed class WorkerPipeServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<WorkerRequest, Task<WorkerResponse>> _handler;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<Task> _connections = new();

    public WorkerPipeServer(Func<WorkerRequest, Task<WorkerResponse>> handler)
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
            var server = new NamedPipeServerStream(
                WorkerPipeConstants.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

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

        var requestJson = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return;
        }

        WorkerResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<WorkerRequest>(requestJson, JsonOptions) ?? new WorkerRequest();
            response = await _handler(request);
        }
        catch (Exception exception)
        {
            response = new WorkerResponse
            {
                Success = false,
                Error = exception.Message
            };
        }

        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        await writer.WriteLineAsync(responseJson);
    }
}
