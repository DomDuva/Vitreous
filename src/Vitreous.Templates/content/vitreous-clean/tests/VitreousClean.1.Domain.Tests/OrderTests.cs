using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Domain.Tests;

/// <summary>The business rules need no database, no host and no fakes: the domain is plain classes.</summary>
public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Placing_an_order_records_an_event()
    {
        var order = Order.Place("ada@example.com", [Line(quantity: 2, price: 49.90m), Line(quantity: 1, price: 10m)], Now).Value;

        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(109.80m, order.Total.Amount);
        Assert.Equal([new OrderPlaced(order.Id, "ada@example.com", order.Total)], order.DequeueDomainEvents());
        Assert.Empty(order.DequeueDomainEvents());
    }

    [Fact]
    public void An_order_without_lines_is_rejected()
    {
        var result = Order.Place("ada@example.com", [], Now);

        Assert.Equal("VALIDATION", result.Error.Code);
    }

    [Fact]
    public void An_order_with_too_many_lines_is_rejected()
    {
        var result = Order.Place("ada@example.com", [.. Enumerable.Range(0, Order.MaxLines + 1).Select(_ => Line())], Now);

        Assert.Equal("VALIDATION", result.Error.Code);
    }

    [Fact]
    public void An_order_in_two_currencies_is_rejected()
    {
        var result = Order.Place("ada@example.com", [Line(currency: "EUR"), Line(currency: "USD")], Now);

        Assert.Equal("VALIDATION", result.Error.Code);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("Keyboard", 0)]
    [InlineData("Keyboard", OrderLine.MaxQuantity + 1)]
    public void An_invalid_line_is_rejected(string product, int quantity)
    {
        var result = OrderLine.Create(product, quantity, Money.Create(1m, "EUR").Value);

        Assert.Equal("VALIDATION", result.Error.Code);
    }

    [Fact]
    public void An_order_ships_once()
    {
        var order = Order.Place("ada@example.com", [Line()], Now).Value;
        order.DequeueDomainEvents();

        var first  = order.Ship(Now.AddDays(1));
        var second = order.Ship(Now.AddDays(2));

        Assert.True(first.IsSuccess);
        Assert.Equal("CONFLICT", second.Error.Code);
        Assert.Equal(Now.AddDays(1), order.ShippedAt);
        Assert.Equal([new OrderShipped(order.Id, "ada@example.com")], order.DequeueDomainEvents());
    }

    private static OrderLine Line(int quantity = 1, decimal price = 1m, string currency = "EUR") =>
        OrderLine.Create("Keyboard", quantity, Money.Create(price, currency).Value).Value;
}
