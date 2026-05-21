using System.ComponentModel;
using Vitreous.Abstractions;

namespace Vitreous.Internal;

/// <summary>
/// Concurrent <see cref="IPublisher"/> implementation.
/// All handlers are started simultaneously via <see cref="Task.WhenAll"/>;
/// every handler runs to completion regardless of individual failures,
/// and all exceptions are re-thrown as an <see cref="AggregateException"/>.
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
                return list[0].Handle(notification, ct);

            return RunHandlersInParallelAsync(handlers, notification, ct);
        }

        return PublishWithBehaviorsAsync(notification, handlers, behaviors, ct);
    }

    private static async ValueTask RunHandlersInParallelAsync<TNotification>(
        IEnumerable<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken ct)
        where TNotification : INotification
    {
        var tasks = handlers
            .Select(h => h.Handle(notification, ct).AsTask())
            .ToArray();

        var whenAll = Task.WhenAll(tasks);
        try
        {
            await whenAll;
        }
        catch
        {
            throw whenAll.Exception!;
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
