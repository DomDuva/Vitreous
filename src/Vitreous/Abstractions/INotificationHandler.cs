namespace Vitreous.Abstractions;

/// <summary>
/// Handles a notification of type <typeparamref name="TNotification"/>.
/// Multiple handlers may be registered for the same notification type.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationHandler<TNotification>
    where TNotification : INotification
{
    /// <summary>Handles the given <paramref name="notification"/>.</summary>
    ValueTask Handle(TNotification notification, CancellationToken ct = default);
}
