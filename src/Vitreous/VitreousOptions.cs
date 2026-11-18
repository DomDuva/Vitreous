namespace Vitreous;

/// <summary>
/// Configuration options for the Vitreous mediator.
/// </summary>
public sealed class VitreousOptions
{
    /// <summary>
    /// Default service lifetime for registered handlers.
    /// Defaults to <see cref="ServiceLifetime.Scoped"/>, which is safe for handlers that depend on
    /// scoped services such as a <c>DbContext</c>. <c>ISender</c> and <c>IPublisher</c> are then
    /// scoped too, so resolve them from a scope (minimal APIs and controllers do this automatically).
    /// Use <see cref="ServiceLifetime.Singleton"/> only when every handler is stateless and has no
    /// scoped dependencies; the dispatcher is then a singleton as well.
    /// </summary>
    public ServiceLifetime DefaultHandlerLifetime { get; set; } = ServiceLifetime.Scoped;

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
