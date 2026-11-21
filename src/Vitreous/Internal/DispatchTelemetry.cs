using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Vitreous.Abstractions;

namespace Vitreous.Internal;

/// <summary>
/// Records a span on <see cref="VitreousTelemetry.ActivitySourceName"/> and a
/// <c>vitreous.dispatch.duration</c> measurement on <see cref="VitreousTelemetry.MeterName"/>
/// around a dispatch.
/// </summary>
/// <remarks>
/// This type is <see langword="public"/> solely because the source-generated
/// <c>VitreousDispatcher</c> calls it directly. It is not intended for direct
/// use by application code.
/// <para>
/// Callers check <see cref="IsEnabled"/> and call the invoker themselves when it is
/// <see langword="false"/>, so a dispatch nobody listens to costs one check.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DispatchTelemetry
{
    private static readonly ActivitySource s_source = VitreousTelemetry.ActivitySource;

    private static readonly Histogram<double> s_duration = VitreousTelemetry.Meter.CreateHistogram<double>(
        "vitreous.dispatch.duration",
        unit: "s",
        description: "Duration of a Vitreous dispatch: from Send/CreateStream/Publish to its result, stream end or last handler."
#if NET9_0_OR_GREATER
        // The SDK default buckets (0, 5, 10, 25, ...) are meant for milliseconds; a dispatch is
        // reported in seconds and is often well under one.
        , tags: null
        , advice: new InstrumentAdvice<double>
        {
            HistogramBucketBoundaries = [0.0001, 0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10],
        }
#endif
        );

    // Span names are built once per (kind, message type) and once per handler type.
    private static readonly ConcurrentDictionary<(MessageKind Kind, Type Type), string> s_spanNames = new();
    private static readonly ConcurrentDictionary<Type, string> s_handlerSpanNames = new();

    internal const string MessageTypeTag = "vitreous.message.type";
    internal const string MessageKindTag = "vitreous.message.kind";
    internal const string OutcomeTag     = "vitreous.dispatch.outcome";
    internal const string HandlerTypeTag = "vitreous.handler.type";
    internal const string StreamItemsTag = "vitreous.stream.items";
    internal const string ErrorTypeTag   = "error.type";

    /// <summary>
    /// <see langword="true"/> when something listens to the Vitreous activity source or the
    /// dispatch duration instrument.
    /// </summary>
    public static bool IsEnabled => s_source.HasListeners() || s_duration.Enabled;

    /// <summary>Dispatches a command through <paramref name="invoker"/> and records it.</summary>
    public static ValueTask<Result<TResult>> SendCommand<TResult>(
        Func<IServiceProvider, ICommand<TResult>, CancellationToken, ValueTask<Result<TResult>>> invoker,
        IServiceProvider services,
        ICommand<TResult> command,
        CancellationToken ct) =>
        Send(invoker, services, command, MessageKind.Command, ct);

    /// <summary>Dispatches a void command through <paramref name="invoker"/> and records it.</summary>
    public static async ValueTask<Result> SendCommand(
        Func<IServiceProvider, ICommand, CancellationToken, ValueTask<Result>> invoker,
        IServiceProvider services,
        ICommand command,
        CancellationToken ct)
    {
        var scope = DispatchScope.Start(MessageKind.Command, command.GetType());
        Result result;
        try
        {
            result = await invoker(services, command, ct);
        }
        catch (Exception ex)
        {
            scope.Fail(ex, IsCancellation(ex, ct));
            throw;
        }

        scope.Complete(result.IsSuccess ? null : result.Error);
        return result;
    }

    /// <summary>Dispatches a query through <paramref name="invoker"/> and records it.</summary>
    public static ValueTask<Result<TResult>> SendQuery<TResult>(
        Func<IServiceProvider, IQuery<TResult>, CancellationToken, ValueTask<Result<TResult>>> invoker,
        IServiceProvider services,
        IQuery<TResult> query,
        CancellationToken ct) =>
        Send(invoker, services, query, MessageKind.Query, ct);

    /// <summary>
    /// Opens a stream through <paramref name="invoker"/> and records it. The span stays open
    /// until the returned stream's enumerator is disposed, so a stream that is never
    /// enumerated never ends its span.
    /// </summary>
    public static async ValueTask<Result<IAsyncEnumerable<TResult>>> CreateStream<TResult>(
        Func<IServiceProvider, IStreamQuery<TResult>, CancellationToken, ValueTask<Result<IAsyncEnumerable<TResult>>>> invoker,
        IServiceProvider services,
        IStreamQuery<TResult> query,
        CancellationToken ct)
    {
        var scope = DispatchScope.Start(MessageKind.Stream, query.GetType());
        Result<IAsyncEnumerable<TResult>> result;
        try
        {
            result = await invoker(services, query, ct);
        }
        catch (Exception ex)
        {
            scope.Fail(ex, IsCancellation(ex, ct));
            throw;
        }

        if (result.IsFailure)
        {
            scope.Complete(result.Error);
            return result;
        }

        return Result<IAsyncEnumerable<TResult>>.Ok(new TelemetryAsyncEnumerable<TResult>(result.Value, scope, ct));
    }

    private static async ValueTask<Result<TResult>> Send<TMessage, TResult>(
        Func<IServiceProvider, TMessage, CancellationToken, ValueTask<Result<TResult>>> invoker,
        IServiceProvider services,
        TMessage message,
        MessageKind kind,
        CancellationToken ct)
        where TMessage : notnull
    {
        // An async method: Activity.Current is set inside it, and an async method's changes to
        // the execution context never flow back to its caller.
        var scope = DispatchScope.Start(kind, message.GetType());
        Result<TResult> result;
        try
        {
            result = await invoker(services, message, ct);
        }
        catch (Exception ex)
        {
            scope.Fail(ex, IsCancellation(ex, ct));
            throw;
        }

        scope.Complete(result.IsSuccess ? null : result.Error);
        return result;
    }

    // -------------------------------------------------------------------------
    // Notifications (called by SequentialPublisher / ParallelPublisher)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs <paramref name="publish"/> under a <c>vitreous publish {Notification}</c> span.
    /// </summary>
    internal static async ValueTask Publish<TState, TNotification>(
        Func<TState, TNotification, CancellationToken, ValueTask> publish,
        TState state,
        TNotification notification,
        CancellationToken ct)
        where TNotification : INotification
    {
        var scope = DispatchScope.Start(MessageKind.Notification, typeof(TNotification));
        try
        {
            await publish(state, notification, ct);
        }
        catch (Exception ex)
        {
            scope.Fail(ex, IsCancellation(ex, ct));
            throw;
        }

        scope.Complete(error: null);
    }

    /// <summary>
    /// Calls <paramref name="handler"/>, under a <c>vitreous handle {Handler}</c> span when
    /// something listens to the activity source.
    /// </summary>
    internal static ValueTask Handle<TNotification>(
        INotificationHandler<TNotification> handler,
        TNotification notification,
        CancellationToken ct)
        where TNotification : INotification =>
        s_source.HasListeners()
            ? HandleTraced(handler, notification, ct)
            : handler.Handle(notification, ct);

    private static async ValueTask HandleTraced<TNotification>(
        INotificationHandler<TNotification> handler,
        TNotification notification,
        CancellationToken ct)
        where TNotification : INotification
    {
        var handlerType = handler.GetType();
        // Parented to the publish span through Activity.Current, which also flows into
        // handlers that ParallelPublisher runs concurrently.
        using var activity = s_source.StartActivity(
            s_handlerSpanNames.GetOrAdd(handlerType, static t => $"vitreous handle {t.Name}"),
            ActivityKind.Internal);

        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(HandlerTypeTag, handlerType.FullName);
            activity.SetTag(MessageTypeTag, typeof(TNotification).FullName);
            activity.SetTag(MessageKindTag, KindName(MessageKind.Notification));
        }

        try
        {
            await handler.Handle(notification, ct);
        }
        catch (Exception ex)
        {
            if (activity is not null)
                RecordException(activity, ex, IsCancellation(ex, ct));
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Shared
    // -------------------------------------------------------------------------

    internal static bool IsCancellation(Exception ex, CancellationToken ct) =>
        ex is OperationCanceledException && ct.IsCancellationRequested;

    private static string KindName(MessageKind kind) => kind switch
    {
        MessageKind.Command      => "command",
        MessageKind.Query        => "query",
        MessageKind.Stream       => "stream",
        MessageKind.Notification => "notification",
        _                        => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string SpanName(MessageKind kind, Type messageType) =>
        s_spanNames.GetOrAdd((kind, messageType), static key =>
            (key.Kind == MessageKind.Notification ? "vitreous publish " : $"vitreous {KindName(key.Kind)} ") + key.Type.Name);

    /// <summary>
    /// Marks <paramref name="activity"/> as failed by <paramref name="ex"/>, or leaves its
    /// status unset when the caller canceled.
    /// </summary>
    private static void RecordException(Activity activity, Exception ex, bool canceled)
    {
        if (canceled)
            return;

        activity.SetStatus(ActivityStatusCode.Error);
        if (!activity.IsAllDataRequested)
            return;

        activity.SetTag(ErrorTypeTag, ex.GetType().FullName);
#if NET9_0_OR_GREATER
        activity.AddException(ex);
#else
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            { "exception.type",       ex.GetType().FullName },
            { "exception.message",    ex.Message },
            { "exception.stacktrace", ex.ToString() },
        }));
#endif
    }

    internal enum MessageKind { Command, Query, Stream, Notification }

    /// <summary>
    /// One dispatch in flight: the span (when sampled) and the start timestamp for the
    /// duration measurement. Exactly one of <see cref="Complete"/> or <see cref="Fail"/> ends it.
    /// </summary>
    internal readonly struct DispatchScope
    {
        private readonly long _start;
        private readonly MessageKind _kind;
        private readonly Type _messageType;

        private DispatchScope(Activity? activity, long start, MessageKind kind, Type messageType)
        {
            Activity     = activity;
            _start       = start;
            _kind        = kind;
            _messageType = messageType;
        }

        public Activity? Activity { get; }

        public static DispatchScope Start(MessageKind kind, Type messageType)
        {
            var start = Stopwatch.GetTimestamp();

            // Build the span name only when there is a listener to hand it to.
            var activity = s_source.HasListeners()
                ? s_source.StartActivity(SpanName(kind, messageType), ActivityKind.Internal)
                : null;

            if (activity is { IsAllDataRequested: true })
            {
                activity.SetTag(MessageTypeTag, messageType.FullName);
                activity.SetTag(MessageKindTag, KindName(kind));
            }

            return new DispatchScope(activity, start, kind, messageType);
        }

        /// <summary>Ends a dispatch that returned a result: a success, or a failed <see cref="Result"/>.</summary>
        public void Complete(Error? error)
        {
            if (error is null)
            {
                End("success", errorType: null);
                return;
            }

            // A failed Result is an error for the span (the dispatch did not do its job), but
            // only Error.Code is recorded: Error.Extensions may carry user data.
            Activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            if (Activity is { IsAllDataRequested: true })
                Activity.SetTag(ErrorTypeTag, error.Code);
            End("failure", error.Code);
        }

        /// <summary>Ends a dispatch that threw. A canceled dispatch is not an error.</summary>
        public void Fail(Exception ex, bool canceled)
        {
            if (Activity is not null)
                RecordException(Activity, ex, canceled);

            if (canceled)
                End("canceled", errorType: null);
            else
                End("exception", ex.GetType().FullName);
        }

        private void End(string outcome, string? errorType)
        {
            if (Activity is not null)
            {
                if (Activity.IsAllDataRequested)
                    Activity.SetTag(OutcomeTag, outcome);
                Activity.Dispose();
            }

            if (!s_duration.Enabled)
                return;

            var tags = new TagList
            {
                { MessageKindTag, KindName(_kind) },
                { MessageTypeTag, _messageType.FullName },
                { OutcomeTag,     outcome },
            };
            if (errorType is not null)
                tags.Add(ErrorTypeTag, errorType);

            s_duration.Record(Stopwatch.GetElapsedTime(_start).TotalSeconds, tags);
        }
    }
}
