using Vitreous.Abstractions;

namespace Vitreous.Internal;

/// <summary>
/// Sequential, fail-fast <see cref="IPublisher"/> implementation.
/// Handlers are invoked one after another; the first failure stops the chain.
/// </summary>
public sealed class SequentialPublisher : IPublisher
{
    private readonly IServiceProvider _services;

    public SequentialPublisher(IServiceProvider services) => _services = services;

    public async ValueTask Publish<TNotification>(
        TNotification notification,
        CancellationToken ct = default)
        where TNotification : INotification
    {
        var handlers = _services
            .GetServices<INotificationHandler<TNotification>>()
            .ToArray();

        var behaviors = _services
            .GetServices<INotificationPipelineBehavior<TNotification>>()
            .ToArray();

        // Terminal: run handlers sequentially, stop on first exception
        NotificationHandlerDelegate<TNotification> terminal = async (n, token) =>
        {
            foreach (var handler in handlers)
                await handler.Handle(n, token);
        };

        await PipelineExecutor.ExecuteNotification(notification, terminal, behaviors, ct);
    }
}
