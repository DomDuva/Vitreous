using Microsoft.EntityFrameworkCore;
using Vitreous.Abstractions;

namespace Vitreous.Sample.Orders.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record OrderDto(Guid Id, string CustomerName, string Product, int Quantity, DateTimeOffset CreatedAt);

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

public sealed record ListOrdersQuery : IQuery<IReadOnlyList<OrderDto>>;

// ── Handlers ──────────────────────────────────────────────────────────────────

public sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderDto>
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<GetOrderQueryHandler> _logger;

    public GetOrderQueryHandler(OrdersDbContext db, ILogger<GetOrderQueryHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async ValueTask<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct = default)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == query.OrderId)
            .Select(o => new OrderDto(o.Id, o.CustomerName, o.Product, o.Quantity, o.CreatedAt))
            .FirstOrDefaultAsync(ct);

        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} not found.", query.OrderId);
            return Error.NotFound($"Order '{query.OrderId}' not found.");
        }

        _logger.LogInformation("Fetched order {OrderId}.", order.Id);
        return order;
    }
}

public sealed class ListOrdersQueryHandler : IQueryHandler<ListOrdersQuery, IReadOnlyList<OrderDto>>
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<ListOrdersQueryHandler> _logger;

    public ListOrdersQueryHandler(OrdersDbContext db, ILogger<ListOrdersQueryHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async ValueTask<Result<IReadOnlyList<OrderDto>>> Handle(
        ListOrdersQuery query,
        CancellationToken ct = default)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderDto(o.Id, o.CustomerName, o.Product, o.Quantity, o.CreatedAt))
            .ToListAsync(ct);

        _logger.LogInformation("Listed {Count} orders.", orders.Count);

        return orders;
    }
}
