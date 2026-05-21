namespace Vitreous;

/// <summary>
/// Configuration options for the Vitreous mediator.
/// </summary>
public sealed class VitreousOptions
{
    /// <summary>
    /// Default service lifetime for registered handlers.
    /// Defaults to <see cref="ServiceLifetime.Singleton"/> — appropriate for stateless handlers.
    /// </summary>
    public ServiceLifetime DefaultHandlerLifetime { get; set; } = ServiceLifetime.Singleton;

    /// <summary>
    /// Controls how notification handlers are invoked when
    /// <see cref="Vitreous.Abstractions.IPublisher.Publish{TNotification}"/> is called.
    /// Defaults to <see cref="NotificationPublishStrategy.Sequential"/> (fail-fast, in-order).
    /// Set to <see cref="NotificationPublishStrategy.Parallel"/> to fan-out all handlers
    /// concurrently and collect all exceptions as an <see cref="AggregateException"/>.
    /// </summary>
    public global::Vitreous.NotificationPublishStrategy NotificationPublishStrategy { get; set; }
        = global::Vitreous.NotificationPublishStrategy.Sequential;
}
