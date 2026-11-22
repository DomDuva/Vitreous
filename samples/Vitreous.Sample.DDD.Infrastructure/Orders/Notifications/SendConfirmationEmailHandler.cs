using Microsoft.Extensions.Logging;
using Vitreous.Abstractions;
using Vitreous.Sample.DDD.Domain.Orders;

namespace Vitreous.Sample.DDD.Infrastructure.Orders.Notifications;

/// <summary>
/// Infrastructure-layer handler for the <see cref="OrderCreated"/> domain event: simulates sending
/// a confirmation email.
/// Lives in a separate assembly from the Domain and Application layers, demonstrating that the Vitreous
/// source generator discovers handlers across all referenced projects automatically.
/// </summary>
public sealed class SendConfirmationEmailHandler : INotificationHandler<OrderCreated>
{
    private readonly ILogger<SendConfirmationEmailHandler> _logger;

    public SendConfirmationEmailHandler(ILogger<SendConfirmationEmailHandler> logger)
        => _logger = logger;

    public ValueTask Handle(OrderCreated notification, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[Infrastructure] Confirmation email sent to customer '{Customer}' for order {OrderId}.",
            notification.CustomerName, notification.OrderId);

        return ValueTask.CompletedTask;
    }
}
