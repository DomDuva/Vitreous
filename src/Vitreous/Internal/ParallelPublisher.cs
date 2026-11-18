using System.ComponentModel;
using Vitreous.Abstractions;

namespace Vitreous.Internal;

/// <summary>
/// Concurrent <see cref="IPublisher"/> implementation.
/// All handlers are started before any is awaited; every handler runs to completion
/// regardless of individual failures (including handlers that throw synchronously),
/// and all exceptions are re-thrown as an <see cref="AggregateException"/>. When handlers
/// were only canceled, an <see cref="OperationCanceledException"/> is thrown instead.
/// </summary>
/// <remarks>
/// This type is <see langword="public"/> solely because the source-generated
/// <c>VitreousDispatcher</c> may resolve it by concrete type via
/// <c>GetRequiredService&lt;ParallelPublisher&gt;()</c>. It is not intended
/// for direct use by application code.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ParallelPublisher : IPublisher
{
    private readonly IServiceProvider _services;

    public ParallelPublisher(IServiceProvider services) => _services = services;

    public ValueTask Publish<TNotification>(
        TNotification notification,
        CancellationToken ct = default)
        where TNotification : INotification
    {
        var handlers = _services.GetServices<INotificationHandler<TNotification>>();

        if (handlers is ICollection<INotificationHandler<TNotification>> { Count: 0 })
            return ValueTask.CompletedTask;

        var behaviors = _services.GetServices<INotificationPipelineBehavior<TNotification>>();

        if (behaviors is ICollection<INotificationPipelineBehavior<TNotification>> { Count: 0 })
        {
            if (handlers is IReadOnlyList<INotificationHandler<TNotification>> { Count: 1 } list)
                return RunSingleHandler(list[0], notification, ct);

            return RunHandlersInParallelAsync(handlers, notification, ct);
        }

        return PublishWithBehaviorsAsync(notification, handlers, behaviors, ct);
    }

    /// <summary>
    /// Single-handler fast path. Returns the handler's <see cref="ValueTask"/> directly when it
    /// completed successfully; otherwise reports failures the same way as the multi-handler path.
    /// </summary>
    private static ValueTask RunSingleHandler<TNotification>(
        INotificationHandler<TNotification> handler,
        TNotification notification,
        CancellationToken ct)
        where TNotification : INotification
    {
        ValueTask pending;
        try
        {
            pending = handler.Handle(notification, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ValueTask.FromException(new AggregateException(ex));
        }

        return pending.IsCompletedSuccessfully ? pending : AwaitSingleAsync(pending);

        static async ValueTask AwaitSingleAsync(ValueTask pending)
        {
            try
            {
                await pending;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AggregateException(ex);
            }
        }
    }

    private static async ValueTask RunHandlersInParallelAsync<TNotification>(
        IEnumerable<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken ct)
        where TNotification : INotification
    {
        // Start every handler before awaiting any of them. A handler that throws
        // synchronously becomes a faulted task, so the others still start.
        var tasks = new List<Task>(handlers is ICollection<INotificationHandler<TNotification>> c ? c.Count : 4);
        foreach (var handler in handlers)
            tasks.Add(Start(handler, notification, ct));

        await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        List<Exception>? errors = null;
        var canceled = false;
        foreach (var task in tasks)
        {
            if (task.IsFaulted)
                (errors ??= new List<Exception>()).AddRange(task.Exception!.InnerExceptions);
            else if (task.IsCanceled)
                canceled = true;
        }

        if (errors is not null)
            throw new AggregateException(errors);

        // Only cancellations: surface them as cancellation rather than as a failure.
        if (canceled)
            throw new OperationCanceledException("One or more notification handlers were canceled.", ct);
    }

    private static Task Start<TNotification>(
        INotificationHandler<TNotification> handler,
        TNotification notification,
        CancellationToken ct)
        where TNotification : INotification
    {
        try
        {
            return handler.Handle(notification, ct).AsTask();
        }
        catch (OperationCanceledException oce)
        {
            return Task.FromCanceled(oce.CancellationToken.IsCancellationRequested ? oce.CancellationToken : new CancellationToken(true));
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private static async ValueTask PublishWithBehaviorsAsync<TNotification>(
        TNotification notification,
        IEnumerable<INotificationHandler<TNotification>> handlers,
        IEnumerable<INotificationPipelineBehavior<TNotification>> behaviors,
        CancellationToken ct)
        where TNotification : INotification
    {
        NotificationHandlerDelegate<TNotification> terminal = (n, token) =>
            RunHandlersInParallelAsync(handlers, n, token);

        await PipelineExecutor.ExecuteNotification(notification, terminal, behaviors, ct);
    }
}
