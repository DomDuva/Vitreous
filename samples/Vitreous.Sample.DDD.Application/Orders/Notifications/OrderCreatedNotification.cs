using Microsoft.Extensions.Logging;
using Vitreous.Abstractions;

namespace Vitreous.Sample.DDD.Application.Orders.Notifications;

// ── Notification ──────────────────────────────────────────────────────────────

public sealed record OrderCreatedNotification(Guid OrderId, string CustomerName, string Product) : INotification;

// ── Application-layer handler ─────────────────────────────────────────────────

/// <summary>
/// Application-layer handler: logs every new order creation.
/// Infrastructure concerns (email, analytics) are handled in Vitreous.Sample.DDD.Infrastructure.
/// </summary>
public sealed class LogOrderCreatedHandler : INotificationHandler<OrderCreatedNotification>
{
    private readonly ILogger<LogOrderCreatedHandler> _logger;

    public LogOrderCreatedHandler(ILogger<LogOrderCreatedHandler> logger)
        => _logger = logger;

    public ValueTask Handle(OrderCreatedNotification notification, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[Application] Order {OrderId} created — customer: '{Customer}', product: '{Product}'.",
            notification.OrderId, notification.CustomerName, notification.Product);

        return ValueTask.CompletedTask;
    }
}
