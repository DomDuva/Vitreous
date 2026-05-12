using Microsoft.Extensions.Logging;

namespace Vitreous.DevUI.Tracing;

/// <summary>
/// Category of dispatched message — used for UI filtering.
/// </summary>
public enum MessageCategory
{
    Command,
    Query,
    Notification,
    StreamQuery
}

/// <summary>
/// Indicates which lifecycle event a stream trace represents.
/// </summary>
public enum StreamPhase
{
    /// <summary>The stream was opened (setup completed, items may start flowing).</summary>
    Opened,

    /// <summary>The stream enumerator was disposed (all items consumed or iteration abandoned).</summary>
    Closed
}

/// <summary>
/// A single log entry captured from an <see cref="ILogger"/> call made inside a handler.
/// </summary>
public sealed record TraceLogEntry(
    LogLevel Level,
    string Message,
    DateTimeOffset Timestamp);

/// <summary>
/// A complete record of one command, query, or notification dispatch.
/// Captured by <see cref="TracingBehavior{TMessage,TResult}"/> and stored in <see cref="TraceStore"/>.
/// </summary>
public sealed class ExecutionTrace
{
    /// <summary>Unique identifier for this trace.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Wall-clock time when dispatch began.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Simple name of the message type (e.g. "CreateOrderCommand").</summary>
    public string MessageType { get; init; } = string.Empty;

    /// <summary>Fully-qualified name of the message type.</summary>
    public string MessageTypeFullName { get; init; } = string.Empty;

    /// <summary>Whether this is a Command, Query, or Notification.</summary>
    public MessageCategory Category { get; init; }

    /// <summary>Simple name of the handler type.</summary>
    public string HandlerType { get; init; } = string.Empty;

    /// <summary>How long the full dispatch (including pipeline) took.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary><see langword="true"/> when <see cref="Result{T}.IsSuccess"/> was true.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// <see langword="true"/> when the result was served from cache rather than from the handler.
    /// Always <see langword="false"/> when <c>Vitreous.Caching</c> is not registered.
    /// </summary>
    public bool IsCacheHit { get; init; }

    /// <summary>Populated when <see cref="IsSuccess"/> is <see langword="false"/>.</summary>
    public ErrorSnapshot? Error { get; init; }

    /// <summary>JSON-serialized input message (may be redacted).</summary>
    public string? SerializedInput { get; init; }

    /// <summary>JSON-serialized output/result (may be redacted).</summary>
    public string? SerializedOutput { get; init; }

    /// <summary>Structured log entries captured from handlers during this dispatch.</summary>
    public IReadOnlyList<TraceLogEntry> Logs { get; init; } = Array.Empty<TraceLogEntry>();

    /// <summary>Optional correlation identifier from <c>Activity.Current</c> or HTTP headers.</summary>
    public string? CorrelationId { get; init; }

    // ── Stream-specific properties ─────────────────────────────────────────────

    /// <summary>
    /// Shared identifier linking the "opened" and "closed" traces of a single stream dispatch.
    /// <see langword="null"/> for non-stream traces.
    /// </summary>
    public Guid? StreamId { get; init; }

    /// <summary>
    /// Indicates whether this trace is the "opened" or "closed" event of a stream.
    /// <see langword="null"/> for non-stream traces.
    /// </summary>
    public StreamPhase? StreamPhase { get; init; }

    /// <summary>
    /// Number of items yielded before the enumerator was disposed.
    /// Only populated on <see cref="Tracing.StreamPhase.Closed"/> traces;
    /// <see langword="null"/> otherwise.
    /// </summary>
    public int? ItemCount { get; init; }
}

/// <summary>
/// A snapshot of an <see cref="Error"/> that is safe to serialize.
/// </summary>
public sealed record ErrorSnapshot(string Code, string Message);
