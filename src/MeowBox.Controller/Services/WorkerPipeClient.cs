using System.IO.Pipes;
using System.Text.Json;
using MeowBox.Core.Contracts;

namespace MeowBox.Controller.Services;

public sealed class WorkerPipeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<WorkerResponse?> SendAsync(WorkerRequest request, int connectTimeoutMs = 1500, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", WorkerPipeConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCancellationTokenSource.CancelAfter(connectTimeoutMs);

            await client.ConnectAsync(linkedCancellationTokenSource.Token);

            using var reader = new StreamReader(client);
            await using var writer = new StreamWriter(client) { AutoFlush = true };

            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            await writer.WriteLineAsync(requestJson);
            var responseJson = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return null;
            }

            return JsonSerializer.Deserialize<WorkerResponse>(responseJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
