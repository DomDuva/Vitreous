using System.Diagnostics;
using System.Text.Json;

namespace Vitreous.DevUI.Tracing;

/// <summary>
/// Wraps an <see cref="IAsyncEnumerable{T}"/> so that every enumerator created from it
/// counts items, serializes them, captures logs, and appends a <see cref="StreamPhase.Closed"/>
/// trace to the <see cref="TraceStore"/> when the enumerator is disposed.
/// </summary>
internal sealed class TracedAsyncEnumerable<T> : IAsyncEnumerable<T>
{
    private readonly IAsyncEnumerable<T> _inner;
    private readonly TraceStore _store;
    private readonly Guid _streamId;
    private readonly string _messageType;
    private readonly string _messageTypeFullName;
    private readonly string? _correlationId;
    private readonly TraceContext _traceCtx;
    private readonly JsonSerializerOptions _jsonOptions;

    internal TracedAsyncEnumerable(
        IAsyncEnumerable<T> inner,
        TraceStore store,
        Guid streamId,
        string messageType,
        string messageTypeFullName,
        string? correlationId,
        TraceContext traceCtx,
        JsonSerializerOptions jsonOptions)
    {
        _inner               = inner;
        _store               = store;
        _streamId            = streamId;
        _messageType         = messageType;
        _messageTypeFullName = messageTypeFullName;
        _correlationId       = correlationId;
        _traceCtx            = traceCtx;
        _jsonOptions         = jsonOptions;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new TracedAsyncEnumerator<T>(
            _inner.GetAsyncEnumerator(cancellationToken),
            _store,
            _streamId,
            _messageType,
            _messageTypeFullName,
            _correlationId,
            _traceCtx,
            _jsonOptions);
}

/// <summary>
/// Counts and serializes items produced by the inner enumerator, captures log entries,
/// and emits a <see cref="StreamPhase.Closed"/> <see cref="ExecutionTrace"/> on disposal.
/// <para>
/// <see cref="TraceLoggerProvider.Current"/> is set on every <see cref="MoveNextAsync"/> call
/// so that <see cref="Microsoft.Extensions.Logging.ILogger"/> calls inside the iterator body
/// are captured. The <c>AsyncLocal</c> write is scoped to this async call-chain branch and
/// does not leak back to the caller.
/// </para>
/// </summary>
internal sealed class TracedAsyncEnumerator<T> : IAsyncEnumerator<T>
{
    private readonly IAsyncEnumerator<T> _inner;
    private readonly TraceStore _store;
    private readonly Guid _streamId;
    private readonly string _messageType;
    private readonly string _messageTypeFullName;
    private readonly string? _correlationId;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly TraceContext _traceCtx;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly List<string> _capturedItems = new();
    private int _itemCount;

    internal TracedAsyncEnumerator(
        IAsyncEnumerator<T> inner,
        TraceStore store,
        Guid streamId,
        string messageType,
        string messageTypeFullName,
        string? correlationId,
        TraceContext traceCtx,
        JsonSerializerOptions jsonOptions)
    {
        _inner               = inner;
        _store               = store;
        _streamId            = streamId;
        _messageType         = messageType;
        _messageTypeFullName = messageTypeFullName;
        _correlationId       = correlationId;
        _traceCtx            = traceCtx;
        _jsonOptions         = jsonOptions;
    }

    public T Current => _inner.Current;

    public async ValueTask<bool> MoveNextAsync()
    {
        // Activate the trace context for this async call chain so ILogger calls
        // inside the iterator body are routed to _traceCtx.
        TraceLoggerProvider.Current = _traceCtx;
        try
        {
            var hasNext = await _inner.MoveNextAsync();
            if (hasNext)
            {
                _itemCount++;
                try { _capturedItems.Add(JsonSerializer.Serialize(_inner.Current, _jsonOptions)); }
                catch { /* serialization failure is non-fatal */ }
            }
            return hasNext;
        }
        finally
        {
            TraceLoggerProvider.Current = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        _sw.Stop();

        string? serializedOutput = null;
        if (_capturedItems.Count > 0)
        {
            try { serializedOutput = $"[{string.Join(",", _capturedItems)}]"; }
            catch { /* ignore */ }
        }

        _store.Append(new ExecutionTrace
        {
            MessageType         = _messageType,
            MessageTypeFullName = _messageTypeFullName,
            Category            = MessageCategory.StreamQuery,
            HandlerType         = string.Empty,
            Duration            = _sw.Elapsed,
            IsSuccess           = true,
            StreamId            = _streamId,
            StreamPhase         = StreamPhase.Closed,
            ItemCount           = _itemCount,
            SerializedOutput    = serializedOutput,
            Logs                = _traceCtx.GetLogs(),
            CorrelationId       = _correlationId
        });
    }
}
