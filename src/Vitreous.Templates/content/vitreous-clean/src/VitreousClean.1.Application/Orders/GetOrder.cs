using Microsoft.EntityFrameworkCore;
using Vitreous;
using Vitreous.Abstractions;
#if (UseCaching)
using Vitreous.Caching.Abstractions;
#endif
using VitreousClean._1.Application.Abstractions;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Application.Orders;

#if (UseCaching)
// ICacheableQuery: the caching behavior returns the stored result for CacheKey and only runs the
// handler on a miss. ShipOrderCommand evicts the key when the order changes.
public sealed record GetOrderQuery(Guid OrderId) : ICacheableQuery<OrderDto>
{
    public static string CacheKeyFor(Guid orderId) => $"orders:{orderId}";

    public string CacheKey => CacheKeyFor(OrderId);
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}
#else
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;
#endif

public sealed class GetOrderHandler(IAppDbContext db) : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async ValueTask<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct = default)
    {
        var id    = new OrderId(query.OrderId);
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);

        return order is null ? Error.NotFound($"Order {id} was not found.") : OrderDto.From(order);
    }
}
