namespace Vitreous.Abstractions;

/// <summary>
/// Publishes notifications to all registered handlers using the configured fan-out strategy
/// (default: sequential, fail-fast).
/// </summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification to all registered <see cref="INotificationHandler{TNotification}"/> instances.
    /// </summary>
    ValueTask Publish<TNotification>(
        TNotification notification,
        CancellationToken ct = default)
        where TNotification : INotification;
}
