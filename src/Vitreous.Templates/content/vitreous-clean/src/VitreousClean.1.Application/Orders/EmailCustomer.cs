using Vitreous.Abstractions;
using VitreousClean._1.Application.Abstractions;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Application.Orders;

// Reacts to the domain events, which are published after the order is saved. The order doesn't
// know about emails, and neither do the commands. Add another INotificationHandler<OrderPlaced>
// in any layer and the generator wires it up.
public sealed class EmailCustomer(IEmailSender email) : INotificationHandler<OrderPlaced>, INotificationHandler<OrderShipped>
{
    public async ValueTask Handle(OrderPlaced placed, CancellationToken ct = default) =>
        await email.Send(placed.CustomerEmail, $"We received your order {placed.OrderId} of {placed.Total}", ct);

    public async ValueTask Handle(OrderShipped shipped, CancellationToken ct = default) =>
        await email.Send(shipped.CustomerEmail, $"Your order {shipped.OrderId} is on its way", ct);
}
