using Microsoft.Extensions.Logging;
using Vitreous.Abstractions;
using Vitreous.Sample.DDD.Application.Orders.Notifications;

namespace Vitreous.Sample.DDD.Infrastructure.Orders.Notifications;

/// <summary>
/// Infrastructure-layer handler: simulates sending a confirmation email when an order is created.
/// Lives in a separate assembly from the Application layer, demonstrating that the Vitreous
/// source generator discovers handlers across all referenced projects automatically.
/// </summary>
public sealed class SendConfirmationEmailHandler : INotificationHandler<OrderCreatedNotification>
{
    private readonly ILogger<SendConfirmationEmailHandler> _logger;

    public SendConfirmationEmailHandler(ILogger<SendConfirmationEmailHandler> logger)
        => _logger = logger;

    public ValueTask Handle(OrderCreatedNotification notification, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[Infrastructure] Confirmation email sent to customer '{Customer}' for order {OrderId}.",
            notification.CustomerName, notification.OrderId);

        return ValueTask.CompletedTask;
    }
}
