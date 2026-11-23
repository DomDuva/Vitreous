using VitreousClean._1.Application.Orders;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Application.Tests;

/// <summary>
/// Handlers are plain classes: create one with its dependencies and call Handle, no host needed.
/// </summary>
public sealed class PlaceOrderTests : IDisposable
{
    private readonly TestDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_order_is_stored_with_its_lines()
    {
        var id = await Place("ada@example.com", new PlaceOrderLine("Keyboard", 2, 49.90m), new PlaceOrderLine("Cable", 1, 5m));

        await using var db = _database.CreateContext();
        var order = await new GetOrderHandler(db).Handle(new GetOrderQuery(id), Ct);

        Assert.Equal(104.80m, order.Value.Total);
        Assert.Equal("EUR", order.Value.Currency);
        Assert.Equal([new OrderLineDto("Keyboard", 2, 49.90m), new OrderLineDto("Cable", 1, 5m)], order.Value.Lines);
    }

    [Fact]
    public async Task Saving_the_order_publishes_its_event()
    {
        var id = await Place("ada@example.com", new PlaceOrderLine("Keyboard", 2, 49.90m));

        var placed = Assert.IsType<OrderPlaced>(Assert.Single(_database.Published));
        Assert.Equal(id, placed.OrderId.Value);
        Assert.Equal("99.80 EUR", placed.Total.ToString());
    }

    [Fact]
    public async Task An_order_that_breaks_a_business_rule_is_not_stored()
    {
        await using var db = _database.CreateContext();

        var result = await new PlaceOrderHandler(db, TimeProvider.System)
            .Handle(new PlaceOrderCommand("ada@example.com", "EUR", []), Ct);

        Assert.Equal("VALIDATION", result.Error.Code);
        Assert.Empty(db.Orders);
        Assert.Empty(_database.Published);
    }
#if (UseValidation)

    [Fact]
    public async Task The_validator_reports_everything_that_is_wrong()
    {
        var command = new PlaceOrderCommand("not an email", "EUR", [new PlaceOrderLine("", 0, -1m)]);

        var result = await new PlaceOrderValidator().ValidateAsync(command, Ct);

        Assert.Equal(
            ["CustomerEmail", "Lines[0].Product", "Lines[0].Quantity", "Lines[0].UnitPrice"],
            result.Errors.Select(e => e.PropertyName));
    }
#endif

    public void Dispose() => _database.Dispose();

    private async Task<Guid> Place(string customerEmail, params PlaceOrderLine[] lines)
    {
        await using var db = _database.CreateContext();

        var result = await new PlaceOrderHandler(db, TimeProvider.System)
            .Handle(new PlaceOrderCommand(customerEmail, "EUR", lines), Ct);

        return result.Value;
    }
}
