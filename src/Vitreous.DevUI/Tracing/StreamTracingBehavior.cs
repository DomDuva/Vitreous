using System.Diagnostics;
using System.Text.Json;
using Vitreous.Abstractions;

namespace Vitreous.DevUI.Tracing;

/// <summary>
/// Stream pipeline behavior that captures two traces per stream dispatch:
/// <list type="bullet">
///   <item>An <b>Opened</b> trace — appended immediately when setup completes (or fails).</item>
///   <item>A <b>Closed</b> trace — appended by <see cref="TracedAsyncEnumerator{T}"/> when
///         the enumerator is disposed, including the total item count and stream duration.</item>
/// </list>
/// Both traces share the same <see cref="ExecutionTrace.StreamId"/>.
/// </summary>
public sealed class StreamTracingBehavior<TMessage, TResult> : IStreamPipelineBehavior<TMessage, TResult>
    where TMessage : IStreamQuery<TResult>
{
    private readonly TraceStore _store;
    private readonly int _maxCapturedItems;
    private readonly JsonSerializerOptions _jsonOptions;

    public StreamTracingBehavior(
        TraceStore store,
        VitreousDevUIOptions? options = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        _store = store;
        _maxCapturedItems = options?.MaxCapturedStreamItems ?? 100;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async ValueTask<Result<IAsyncEnumerable<TResult>>> Handle(
        TMessage message,
        StreamQueryHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        var traceCtx = new TraceContext();
        TraceLoggerProvider.Current = traceCtx;

        var sw = Stopwatch.StartNew();
        Result<IAsyncEnumerable<TResult>> result;

        try
        {
            result = await next(message, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            TraceLoggerProvider.Current = null;

            var streamId = Guid.NewGuid();
            _store.Append(BuildOpenedTrace(
                message, sw.Elapsed,
                isSuccess: false,
                error: new ErrorSnapshot("EXCEPTION", ex.Message),
                streamId: streamId,
                logs: traceCtx.GetLogs()));

            throw;
        }

        sw.Stop();
        TraceLoggerProvider.Current = null;

        var id = Guid.NewGuid();

        if (result.IsFailure)
        {
            // Setup failed — emit one Opened trace with IsSuccess = false; no Closed trace.
            _store.Append(BuildOpenedTrace(
                message, sw.Elapsed,
                isSuccess: false,
                error: new ErrorSnapshot(result.Error.Code, result.Error.Message),
                streamId: id,
                logs: traceCtx.GetLogs()));

            return result;
        }

        // Setup succeeded — emit an Opened trace and wrap the stream.
        _store.Append(BuildOpenedTrace(
            message, sw.Elapsed,
            isSuccess: true,
            error: null,
            streamId: id,
            logs: traceCtx.GetLogs()));

        var tracedStream = new TracedAsyncEnumerable<TResult>(
            result.Value,
            _store,
            id,
            typeof(TMessage).Name,
            typeof(TMessage).FullName ?? typeof(TMessage).Name,
            System.Diagnostics.Activity.Current?.TraceId.ToString(),
            traceCtx,
            _jsonOptions,
            _maxCapturedItems);

        return Result<IAsyncEnumerable<TResult>>.Ok(tracedStream);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ExecutionTrace BuildOpenedTrace(
        TMessage message,
        TimeSpan duration,
        bool isSuccess,
        ErrorSnapshot? error,
        Guid streamId,
        IReadOnlyList<TraceLogEntry> logs) =>
        new()
        {
            MessageType         = typeof(TMessage).Name,
            MessageTypeFullName = typeof(TMessage).FullName ?? typeof(TMessage).Name,
            Category            = MessageCategory.StreamQuery,
            HandlerType         = $"IStreamQueryHandler<{typeof(TMessage).Name}, {typeof(TResult).Name}>",
            Duration            = duration,
            IsSuccess           = isSuccess,
            Error               = error,
            SerializedInput     = TrySerialize(message),
            Logs                = logs,
            CorrelationId       = System.Diagnostics.Activity.Current?.TraceId.ToString(),
            StreamId            = streamId,
            StreamPhase         = StreamPhase.Opened
        };

    private string? TrySerialize<T>(T? value)
    {
        if (value is null) return null;
        try { return JsonSerializer.Serialize(value, _jsonOptions); }
        catch { return null; }
    }
}
