using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Vitreous.DevUI.Tracing;

namespace Vitreous.DevUI.Middleware;

/// <summary>
/// Manages active SSE client connections and broadcasts new traces to all of them.
/// Thread-safe. Handles client disconnects gracefully.
/// </summary>
public sealed class SseConnectionManager : IDisposable
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _clients = new();
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
        Converters           = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Subscribes a new SSE client. Returns a reader channel that yields
    /// serialized SSE event strings. Dispose the returned subscription to disconnect.
    /// </summary>
    public SseSubscription Subscribe()
    {
        var id      = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

        _clients[id] = channel;
        return new SseSubscription(id, channel.Reader, () => Remove(id));
    }

    /// <summary>
    /// Broadcasts a trace to all active SSE clients.
    /// Slow or disconnected clients are removed.
    /// </summary>
    public void Broadcast(ExecutionTrace trace)
    {
        var json    = JsonSerializer.Serialize(trace, _json);
        var payload = $"data: {json}\n\n";

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
    private readonly Guid _id;
    private readonly Action _onDispose;

    public ChannelReader<string> Reader { get; }

    internal SseSubscription(Guid id, ChannelReader<string> reader, Action onDispose)
    {
        _id       = id;
        Reader    = reader;
        _onDispose = onDispose;
    }

    public void Dispose() => _onDispose();
}
