using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Vitreous.Abstractions;
using VitreousClean._1.Application.Abstractions;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Application.Orders;

// A stream query returns its items one at a time (IAsyncEnumerable), as EF Core reads them, so a
// large list never sits in memory all at once.
public sealed record ListOrdersQuery(OrderStatus? Status = null) : IStreamQuery<OrderDto>;

public sealed class ListOrdersHandler(IAppDbContext db) : StreamQueryHandler<ListOrdersQuery, OrderDto>
{
    protected override async IAsyncEnumerable<OrderDto> Stream(
        ListOrdersQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var orders = db.Orders
            .AsNoTracking()
            .Where(o => query.Status == null || o.Status == query.Status)
            .OrderBy(o => o.PlacedAt);

        await foreach (var order in orders.AsAsyncEnumerable().WithCancellation(ct))
            yield return OrderDto.From(order);
    }
}
