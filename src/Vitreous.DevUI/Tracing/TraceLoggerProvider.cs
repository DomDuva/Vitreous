using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Vitreous.DevUI.Tracing;

/// <summary>
/// Ambient context set by <see cref="TracingBehavior{TMessage,TResult}"/> on the current async call stack.
/// Any <see cref="ILogger"/> call made inside a handler writes to both the original logger and this context.
/// </summary>
public sealed class TraceContext
{
    private readonly ConcurrentBag<TraceLogEntry> _logs = new();

    /// <summary>Captures a log entry.</summary>
    internal void AddLog(LogLevel level, string message) =>
        _logs.Add(new TraceLogEntry(level, message, DateTimeOffset.UtcNow));

    /// <summary>Returns all captured log entries in insertion order.</summary>
    internal IReadOnlyList<TraceLogEntry> GetLogs() => _logs.ToArray();
}

/// <summary>
/// <see cref="ILoggerProvider"/> that intercepts log calls made on the current async call stack
/// when a <see cref="TraceContext"/> is active.
/// Zero overhead when no context is set.
/// </summary>
internal sealed class TraceLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// The ambient trace context for the current async execution chain.
    /// Set by <see cref="TracingBehavior{TMessage,TResult}"/> before invoking the handler pipeline.
    /// <para>
    /// Uses <see cref="AsyncLocal{T}"/> (not <c>[ThreadStatic]</c>) so the context
    /// flows correctly across <c>await</c> continuations that resume on a different
    /// thread-pool thread.
    /// </para>
    /// </summary>
    private static readonly AsyncLocal<TraceContext?> _current = new();

    public static TraceContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    public ILogger CreateLogger(string categoryName) =>
        new TraceLogger(categoryName);

    public void Dispose() { }

    // -------------------------------------------------------------------------
    // Inner logger
    // -------------------------------------------------------------------------

    private sealed class TraceLogger : ILogger
    {
        private readonly string _category;

        public TraceLogger(string category) => _category = category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => Current is not null;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (Current is not { } ctx) return;

            var message = formatter(state, exception);
            if (exception is not null)
                message = $"{message} | Exception: {exception.GetType().Name}: {exception.Message}";

            ctx.AddLog(logLevel, message);
        }
    }
}
