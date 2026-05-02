using Vitreous.Abstractions;

namespace Vitreous.Sample.Redis.Orders.Notifications;

// ── Notification ──────────────────────────────────────────────────────────────

public sealed record OrderCreatedNotification(Guid OrderId, string CustomerName, string Product) : INotification;

// ── Handlers (multiple) ───────────────────────────────────────────────────────

/// <summary>Simulates sending a confirmation email.</summary>
public sealed class SendConfirmationEmailHandler : INotificationHandler<OrderCreatedNotification>
{
    private readonly ILogger<SendConfirmationEmailHandler> _logger;

    public SendConfirmationEmailHandler(ILogger<SendConfirmationEmailHandler> logger)
        => _logger = logger;

    public ValueTask Handle(OrderCreatedNotification notification, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Confirmation email sent to customer '{Customer}' for order {OrderId}.",
            notification.CustomerName, notification.OrderId);

        return ValueTask.CompletedTask;
    }
}

/// <summary>Simulates updating an analytics dashboard.</summary>
public sealed class UpdateAnalyticsDashboardHandler : INotificationHandler<OrderCreatedNotification>
{
    private readonly ILogger<UpdateAnalyticsDashboardHandler> _logger;

    public UpdateAnalyticsDashboardHandler(ILogger<UpdateAnalyticsDashboardHandler> logger)
        => _logger = logger;

    public ValueTask Handle(OrderCreatedNotification notification, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Analytics dashboard updated — new order for product '{Product}'.",
            notification.Product);

        return ValueTask.CompletedTask;
    }
}
