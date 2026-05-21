namespace Vitreous;

/// <summary>
/// Controls how <see cref="Vitreous.Abstractions.INotificationHandler{TNotification}"/> instances
/// are invoked when a notification is published.
/// </summary>
public enum NotificationPublishStrategy
{
    /// <summary>
    /// Handlers are invoked one after another in registration order.
    /// The first failure stops the chain (fail-fast).
    /// This is the default.
    /// </summary>
    Sequential = 0,

    /// <summary>
    /// All handlers are started concurrently via <see cref="Task.WhenAll"/>.
    /// Every handler runs to completion regardless of individual failures;
    /// all exceptions are collected and re-thrown as an <see cref="AggregateException"/>.
    /// </summary>
    /// <remarks>
    /// Handlers that share mutable scoped services (e.g. a <c>DbContext</c> injected
    /// from the same DI scope) should be aware that those services will be accessed
    /// concurrently. Use <see cref="Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"/>
    /// inside such handlers to create an isolated child scope when needed.
    /// </remarks>
    Parallel = 1,
}
