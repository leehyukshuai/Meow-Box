using System.IO.Pipes;
using System.Text.Json;
using MeowBox.Core.Contracts;

namespace MeowBox.Worker.Services;

internal sealed class ControllerPipeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<bool> SendAsync(WorkerNotification notification, int connectTimeoutMs = 1200, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", ControllerPipeConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCancellationTokenSource.CancelAfter(connectTimeoutMs);

            await client.ConnectAsync(linkedCancellationTokenSource.Token);

            using var reader = new StreamReader(client);
            await using var writer = new StreamWriter(client) { AutoFlush = true };

            var payload = JsonSerializer.Serialize(notification, JsonOptions);
            await writer.WriteLineAsync(payload);
            var ackJson = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(ackJson))
            {
                return false;
            }

            var ack = JsonSerializer.Deserialize<WorkerNotificationAck>(ackJson, JsonOptions);
            return ack?.Success == true;
        }
        catch
        {
            return false;
        }
    }
}
