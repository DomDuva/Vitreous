using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vitreous.Abstractions;

namespace Vitreous.DevUI.Tracing;

/// <summary>
/// Pipeline behavior that wraps every command/query dispatch,
/// capturing timing, result, serialized input/output, and handler logs.
/// Appends an <see cref="ExecutionTrace"/> to <see cref="TraceStore"/> after each dispatch.
/// </summary>
public sealed class TracingBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
{
    private readonly TraceStore _store;
    private readonly JsonSerializerOptions _jsonOptions;

    public TracingBehavior(TraceStore store, JsonSerializerOptions? jsonOptions = null)
    {
        _store = store;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async ValueTask<Result<TResult>> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        // Determine category
        var category = (message is ICommand || message is ICommand<TResult>)
            ? MessageCategory.Command
            : MessageCategory.Query;

        // Set up trace context so handler logs are captured
        var traceCtx = new TraceContext();
        TraceLoggerProvider.Current = traceCtx;

        // Set up pipeline annotations so inner behaviors can signal metadata back to us
        // (e.g. CachingBehavior marks IsCacheHit = true on a cache hit).
        var annotations = new PipelineAnnotations();
        VitreousContext.Current = annotations;

        var sw = Stopwatch.StartNew();
        Result<TResult> result;

        try
        {
            result = await next(message, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();

            var errorTrace = BuildTrace(message, category, sw.Elapsed,
                isSuccess:   false,
                isCacheHit:  annotations.IsCacheHit,
                error:       new ErrorSnapshot("EXCEPTION", ex.Message),
                output:      null,
                logs:        traceCtx.GetLogs());

            _store.Append(errorTrace);
            throw;
        }
        finally
        {
            TraceLoggerProvider.Current = null;
            VitreousContext.Current     = null;
        }

        sw.Stop();

        var trace = BuildTrace(
            message,
            category,
            sw.Elapsed,
            isSuccess:  result.IsSuccess,
            isCacheHit: annotations.IsCacheHit,
            error:      result.IsFailure ? new ErrorSnapshot(result.Error.Code, result.Error.Message) : null,
            output:     result.IsSuccess ? TrySerialize(result.Value) : null,
            logs:       traceCtx.GetLogs());

        _store.Append(trace);
        return result;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ExecutionTrace BuildTrace(
        TMessage message,
        MessageCategory category,
        TimeSpan duration,
        bool isSuccess,
        bool isCacheHit,
        ErrorSnapshot? error,
        string? output,
        IReadOnlyList<TraceLogEntry> logs) =>
        new()
        {
            MessageType         = typeof(TMessage).Name,
            MessageTypeFullName = typeof(TMessage).FullName ?? typeof(TMessage).Name,
            Category            = category,
            HandlerType         = category switch
            {
                MessageCategory.Command when typeof(TResult) == typeof(Unit)
                    => $"ICommandHandler<{typeof(TMessage).Name}>",
                MessageCategory.Command
                    => $"ICommandHandler<{typeof(TMessage).Name}, {typeof(TResult).Name}>",
                _
                    => $"IQueryHandler<{typeof(TMessage).Name}, {typeof(TResult).Name}>"
            },
            Duration            = duration,
            IsSuccess           = isSuccess,
            IsCacheHit          = isCacheHit,
            Error               = error,
            SerializedInput     = TrySerialize(message),
            SerializedOutput    = output,
            Logs                = logs,
            CorrelationId       = Activity.Current?.TraceId.ToString()
        };

    private string? TrySerialize<T>(T? value)
    {
        if (value is null) return null;
        try
        {
            return JsonSerializer.Serialize(value, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Notification-specific tracing behavior — no Result wrapping.
/// </summary>
public sealed class NotificationTracingBehavior<TNotification> : INotificationPipelineBehavior<TNotification>
    where TNotification : INotification
{
    private readonly TraceStore _store;
    private readonly JsonSerializerOptions _jsonOptions;

    public NotificationTracingBehavior(TraceStore store, JsonSerializerOptions? jsonOptions = null)
    {
        _store = store;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async ValueTask Handle(
        TNotification notification,
        NotificationHandlerDelegate<TNotification> next,
        CancellationToken ct = default)
    {
        var traceCtx = new TraceContext();
        TraceLoggerProvider.Current = traceCtx;

        var sw = Stopwatch.StartNew();
        bool isSuccess = true;
        ErrorSnapshot? error = null;

        try
        {
            await next(notification, ct);
        }
        catch (Exception ex)
        {
            isSuccess = false;
            error = new ErrorSnapshot("EXCEPTION", ex.Message);
            throw;
        }
        finally
        {
            sw.Stop();
            TraceLoggerProvider.Current = null;

            var trace = new ExecutionTrace
            {
                MessageType         = typeof(TNotification).Name,
                MessageTypeFullName = typeof(TNotification).FullName ?? typeof(TNotification).Name,
                Category            = MessageCategory.Notification,
                HandlerType         = $"INotificationHandler<{typeof(TNotification).Name}>",
                Duration            = sw.Elapsed,
                IsSuccess           = isSuccess,
                Error               = error,
                SerializedInput     = TrySerialize(notification),
                SerializedOutput    = null,
                Logs                = traceCtx.GetLogs(),
                CorrelationId       = System.Diagnostics.Activity.Current?.TraceId.ToString()
            };

            _store.Append(trace);
        }
    }

    private string? TrySerialize<T>(T? value)
    {
        if (value is null) return null;
        try { return JsonSerializer.Serialize(value, _jsonOptions); }
        catch { return null; }
    }
}
