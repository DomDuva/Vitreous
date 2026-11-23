using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Application.Orders;

/// <summary>What the queries return. The aggregate itself doesn't leave this layer.</summary>
public sealed record OrderDto(
    Guid Id,
    string CustomerEmail,
    string Status,
    decimal Total,
    string Currency,
    DateTimeOffset PlacedAt,
    DateTimeOffset? ShippedAt,
    IReadOnlyList<OrderLineDto> Lines)
{
    public static OrderDto From(Order order) => new(
        order.Id.Value,
        order.CustomerEmail,
        order.Status.ToString(),
        order.Total.Amount,
        order.Total.Currency,
        order.PlacedAt,
        order.ShippedAt,
        [.. order.Lines.Select(static l => new OrderLineDto(l.Product, l.Quantity, l.UnitPrice.Amount))]);
}

public sealed record OrderLineDto(string Product, int Quantity, decimal UnitPrice);
