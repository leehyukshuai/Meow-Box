using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using FnMappingTool.Core.Contracts;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Worker.Services;

internal sealed class TouchpadStreamServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ConcurrentDictionary<int, ConnectedClient> _clients = new();
    private readonly Func<TouchpadLiveStateSnapshot> _snapshotProvider;
    private int _nextClientId;

    public TouchpadStreamServer(Func<TouchpadLiveStateSnapshot> snapshotProvider)
    {
        _snapshotProvider = snapshotProvider;
        _ = AcceptLoopAsync(_cancellationTokenSource.Token);
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();
        _cancellationTokenSource.Dispose();
    }

    public void Broadcast(TouchpadLiveStateSnapshot snapshot)
    {
        foreach (var client in _clients.Values)
        {
            client.TryQueue(snapshot);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                TouchpadPipeConstants.PipeName,
                PipeDirection.Out,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                var client = new ConnectedClient(Interlocked.Increment(ref _nextClientId), server, RemoveClient);
                _clients[client.Id] = client;
                client.TryQueue(_snapshotProvider());
                client.Start(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                server.Dispose();
                break;
            }
            catch
            {
                server.Dispose();
            }
        }
    }

    private void RemoveClient(int id)
    {
        if (_clients.TryRemove(id, out var client))
        {
            client.Dispose();
        }
    }

    private sealed class ConnectedClient : IDisposable
    {
        private readonly NamedPipeServerStream _stream;
        private readonly Action<int> _onClosed;
        private readonly Channel<TouchpadLiveStateSnapshot> _channel = Channel.CreateBounded<TouchpadLiveStateSnapshot>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

        private StreamWriter? _writer;

        public ConnectedClient(int id, NamedPipeServerStream stream, Action<int> onClosed)
        {
            Id = id;
            _stream = stream;
            _onClosed = onClosed;
        }

        public int Id { get; }

        public void Start(CancellationToken cancellationToken)
        {
            _writer = new StreamWriter(_stream) { AutoFlush = true };
            _ = PumpAsync(cancellationToken);
        }

        public void TryQueue(TouchpadLiveStateSnapshot snapshot)
        {
            _channel.Writer.TryWrite(snapshot);
        }

        public void Dispose()
        {
            _channel.Writer.TryComplete();
            _writer?.Dispose();
            _stream.Dispose();
        }

        private async Task PumpAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (await _channel.Reader.WaitToReadAsync(cancellationToken))
                {
                    while (_channel.Reader.TryRead(out var snapshot))
                    {
                        if (_writer is null || !_stream.IsConnected)
                        {
                            return;
                        }

                        var payload = JsonSerializer.Serialize(snapshot, JsonOptions);
                        await _writer.WriteLineAsync(payload);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _onClosed(Id);
            }
        }
    }
}
