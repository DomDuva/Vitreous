using Microsoft.EntityFrameworkCore;
using Vitreous;
using VitreousClean._1.Application.Orders;
using VitreousClean._1.Domain;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Application.Tests;

public sealed class ShipOrderTests : IDisposable
{
    private readonly TestDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_shipped_order_is_stored_and_published()
    {
        var id = await AddOrder();
        _database.Published.Clear();

        Assert.True((await Ship(id)).IsSuccess);

        Assert.Equal([new OrderShipped(new OrderId(id), "ada@example.com")], _database.Published);
        Assert.Equal([id], await List(OrderStatus.Shipped));
        Assert.Empty(await List(OrderStatus.Placed));
    }

    [Fact]
    public async Task An_order_that_was_shipped_already_is_a_conflict()
    {
        var id = await AddOrder();
        await Ship(id);

        Assert.Equal("CONFLICT", (await Ship(id)).Error.Code);
    }

    [Fact]
    public async Task An_order_that_is_shipped_meanwhile_is_a_conflict()
    {
        var id = await AddOrder();

        // This context read the order before the other one ships it.
        await using var db = _database.CreateContext();
        await db.Orders.SingleAsync(Ct);
        await Ship(id);

        var result = await new ShipOrderHandler(db, TimeProvider.System).Handle(new ShipOrderCommand(id), Ct);

        Assert.Equal("CONFLICT", result.Error.Code);
        Assert.Single(_database.Published.OfType<OrderShipped>());
    }

    [Fact]
    public async Task An_unknown_order_is_not_found()
    {
        Assert.Equal("NOT_FOUND", (await Ship(Guid.NewGuid())).Error.Code);
    }

    [Fact]
    public async Task Orders_are_listed_oldest_first()
    {
        var now    = DateTimeOffset.UtcNow;
        var newest = await AddOrder(now);
        var oldest = await AddOrder(now.AddDays(-1));

        Assert.Equal([oldest, newest], await List(status: null));
    }

    public void Dispose() => _database.Dispose();

    private async Task<Guid> AddOrder(DateTimeOffset? placedAt = null)
    {
        var line  = OrderLine.Create("Keyboard", 1, Money.Create(49.90m, "EUR").Value).Value;
        var order = Order.Place("ada@example.com", [line], placedAt ?? DateTimeOffset.UtcNow).Value;

        await using var db = _database.CreateContext();
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        return order.Id.Value;
    }

    private async Task<Result> Ship(Guid id)
    {
        await using var db = _database.CreateContext();
        return await new ShipOrderHandler(db, TimeProvider.System).Handle(new ShipOrderCommand(id), Ct);
    }

    private async Task<List<Guid>> List(OrderStatus? status)
    {
        await using var db = _database.CreateContext();
        var orders = await new ListOrdersHandler(db).Handle(new ListOrdersQuery(status), Ct);

        var ids = new List<Guid>();
        await foreach (var order in orders.Value.WithCancellation(Ct))
            ids.Add(order.Id);

        return ids;
    }
}
