using System.ComponentModel;
using Vitreous.Abstractions;

namespace Vitreous.Internal;

/// <summary>
/// Sequential, fail-fast <see cref="IPublisher"/> implementation.
/// Handlers are invoked one after another; the first failure stops the chain.
/// </summary>
/// <remarks>
/// This type is <see langword="public"/> solely because the source-generated
/// <c>VitreousDispatcher</c> resolves it by concrete type via
/// <c>GetRequiredService&lt;SequentialPublisher&gt;()</c>. It is not intended
/// for direct use by application code.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SequentialPublisher : IPublisher
{
    private readonly IServiceProvider _services;

    public SequentialPublisher(IServiceProvider services) => _services = services;

    public ValueTask Publish<TNotification>(
        TNotification notification,
        CancellationToken ct = default)
        where TNotification : INotification
    {
        // GetServices returns a T[] from MSDI — avoid .ToArray() which would allocate
        // a second copy. Cast to ICollection<T> for an allocation-free Count check.
        var handlers = _services.GetServices<INotificationHandler<TNotification>>();

        // Fast path: no handlers registered — don't even resolve behaviors.
        if (handlers is ICollection<INotificationHandler<TNotification>> { Count: 0 })
            return ValueTask.CompletedTask;

        var behaviors = _services.GetServices<INotificationPipelineBehavior<TNotification>>();

        // Fast path: no behaviors — invoke handlers without building a delegate chain.
        if (behaviors is ICollection<INotificationPipelineBehavior<TNotification>> { Count: 0 })
        {
            // Most common case: single handler — return its ValueTask directly,
            // no async state machine needed.
            if (handlers is IReadOnlyList<INotificationHandler<TNotification>> { Count: 1 } list)
                return list[0].Handle(notification, ct);

            return RunHandlersSequentiallyAsync(handlers, notification, ct);
        }

        // Slow path: behaviors present — build the full pipeline.
        return PublishWithBehaviorsAsync(notification, handlers, behaviors, ct);
    }

    private static async ValueTask RunHandlersSequentiallyAsync<TNotification>(
        IEnumerable<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken ct)
        where TNotification : INotification
    {
        foreach (var handler in handlers)
            await handler.Handle(notification, ct);
    }

    private static async ValueTask PublishWithBehaviorsAsync<TNotification>(
        TNotification notification,
        IEnumerable<INotificationHandler<TNotification>> handlers,
        IEnumerable<INotificationPipelineBehavior<TNotification>> behaviors,
        CancellationToken ct)
        where TNotification : INotification
    {
        // Terminal: run handlers sequentially, stop on first exception.
        NotificationHandlerDelegate<TNotification> terminal = async (n, token) =>
        {
            foreach (var handler in handlers)
                await handler.Handle(n, token);
        };

        await PipelineExecutor.ExecuteNotification(notification, terminal, behaviors, ct);
    }
}
