using System.Collections.Concurrent;
using System.Threading.Channels;
using Vitreous.DevUI.Tracing;

namespace Vitreous.DevUI.Middleware;

/// <summary>
/// Manages active SSE client connections and broadcasts new traces to all of them.
/// Thread-safe. Each client has a bounded buffer; a client that falls that far behind is
/// disconnected (its browser reconnects and replays the buffer).
/// </summary>
public sealed class SseConnectionManager : IDisposable
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _clients = new();
    private readonly DevUISerializer _serializer;
    private readonly int _clientBufferCapacity;

    public SseConnectionManager(DevUISerializer serializer, VitreousDevUIOptions options)
    {
        _serializer           = serializer;
        _clientBufferCapacity = Math.Max(16, options.TraceBufferCapacity);
    }

    /// <summary>Number of connected clients.</summary>
    public int ClientCount => _clients.Count;

    /// <summary>
    /// Subscribes a new SSE client. Returns a reader channel that yields
    /// serialized SSE event strings. Dispose the returned subscription to disconnect.
    /// </summary>
    public SseSubscription Subscribe()
    {
        var id      = Guid.NewGuid();
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(_clientBufferCapacity)
        {
            // Wait (rather than drop) so TryWrite fails when the buffer is full and the slow
            // client is removed. Traces are broadcast from many request threads at once.
            FullMode     = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        _clients[id] = channel;
        return new SseSubscription(channel.Reader, () => Remove(id));
    }

    /// <summary>
    /// Broadcasts a trace to all active SSE clients.
    /// Clients whose buffer is full are removed.
    /// </summary>
    public void Broadcast(ExecutionTrace trace)
    {
        // Nobody listening: don't pay for serialization on the dispatch path.
        if (_clients.IsEmpty)
            return;

        var payload = $"data: {_serializer.SerializeTrace(trace)}\n\n";

        foreach (var (id, channel) in _clients)
        {
            if (!channel.Writer.TryWrite(payload))
                Remove(id);
        }
    }

    private void Remove(Guid id)
    {
        if (_clients.TryRemove(id, out var ch))
            ch.Writer.TryComplete();
    }

    public void Dispose()
    {
        foreach (var ch in _clients.Values)
            ch.Writer.TryComplete();
        _clients.Clear();
    }
}

/// <summary>
/// Represents an active SSE subscription for one client.
/// Disposing it removes the client from <see cref="SseConnectionManager"/>.
/// </summary>
public sealed class SseSubscription : IDisposable
{
    private readonly Action _onDispose;

    public ChannelReader<string> Reader { get; }

    internal SseSubscription(ChannelReader<string> reader, Action onDispose)
    {
        Reader     = reader;
        _onDispose = onDispose;
    }

    public void Dispose() => _onDispose();
}
