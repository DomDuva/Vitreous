using Microsoft.Extensions.Logging;
using Vitreous.Abstractions;
using Vitreous.Sample.DDD.Domain.Orders;

namespace Vitreous.Sample.DDD.Application.Orders.Notifications;

/// <summary>
/// Application-layer handler for the <see cref="OrderCreated"/> domain event: logs every new order.
/// Infrastructure concerns (email, analytics) are handled in Vitreous.Sample.DDD.Infrastructure.
/// </summary>
public sealed class LogOrderCreatedHandler : INotificationHandler<OrderCreated>
{
    private readonly ILogger<LogOrderCreatedHandler> _logger;

    public LogOrderCreatedHandler(ILogger<LogOrderCreatedHandler> logger)
        => _logger = logger;

    public ValueTask Handle(OrderCreated notification, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[Application] Order {OrderId} created — customer: '{Customer}', product: '{Product}'.",
            notification.OrderId, notification.CustomerName, notification.Product);

        return ValueTask.CompletedTask;
    }
}
