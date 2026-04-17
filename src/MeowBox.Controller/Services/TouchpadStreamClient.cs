using System.IO.Pipes;
using System.Text.Json;
using MeowBox.Core.Contracts;
using MeowBox.Core.Models;

namespace MeowBox.Controller.Services;

public sealed class TouchpadStreamClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listenTask;

    public event EventHandler<TouchpadLiveStateSnapshot>? SnapshotReceived;
    public event EventHandler<bool>? ConnectionChanged;

    public bool IsRunning => _cancellationTokenSource is not null;

    public void Start()
    {
        if (_cancellationTokenSource is not null)
        {
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _listenTask = ListenLoopAsync(_cancellationTokenSource.Token);
    }

    public void Stop()
    {
        if (_cancellationTokenSource is null)
        {
            return;
        }

        var cancellationTokenSource = _cancellationTokenSource;
        var listenTask = _listenTask;
        _cancellationTokenSource = null;
        _listenTask = null;

        cancellationTokenSource.Cancel();
        ConnectionChanged?.Invoke(this, false);
        _ = CompleteStopAsync(cancellationTokenSource, listenTask);
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        var notifiedConnected = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", TouchpadPipeConstants.PipeName, PipeDirection.In, PipeOptions.Asynchronous);
                await client.ConnectAsync(1500, cancellationToken);
                using var reader = new StreamReader(client);

                if (!notifiedConnected)
                {
                    notifiedConnected = true;
                    ConnectionChanged?.Invoke(this, true);
                }

                while (!cancellationToken.IsCancellationRequested && client.IsConnected)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        break;
                    }

                    var snapshot = JsonSerializer.Deserialize<TouchpadLiveStateSnapshot>(line, JsonOptions);
                    if (snapshot is not null)
                    {
                        SnapshotReceived?.Invoke(this, snapshot);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
            }

            if (notifiedConnected)
            {
                notifiedConnected = false;
                ConnectionChanged?.Invoke(this, false);
            }

            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task CompleteStopAsync(CancellationTokenSource cancellationTokenSource, Task? listenTask)
    {
        try
        {
            if (listenTask is not null)
            {
                await listenTask.ConfigureAwait(false);
            }
        }
        catch
        {
        }
        finally
        {
            cancellationTokenSource.Dispose();
        }
    }
}
