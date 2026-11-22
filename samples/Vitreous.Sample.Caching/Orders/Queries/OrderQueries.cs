using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Vitreous.Abstractions;
using Vitreous.Caching.Abstractions;

namespace Vitreous.Sample.Caching.Orders.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record OrderDto(Guid Id, string CustomerName, string Product, int Quantity, DateTimeOffset CreatedAt);

// ── Queries ───────────────────────────────────────────────────────────────────

/// <summary>
/// Fetches a single order by ID. Results are cached for 5 minutes per order ID.
/// DeleteOrderCommand invalidates this entry via its direct cache key.
/// </summary>
public sealed record GetOrderQuery(Guid OrderId) : ICacheableQuery<OrderDto>
{
    public string CacheKey => $"order:{OrderId}";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}

/// <summary>
/// Returns all orders. Results are cached for 2 minutes.
/// CreateOrderCommand and DeleteOrderCommand both bust this entry via the direct key.
/// </summary>
public sealed record ListOrdersQuery : ICacheableQuery<IReadOnlyList<OrderDto>>
{
    public string CacheKey => "orders:list";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(2);
    public TimeSpan? SlidingExpiration => null;
}

// ── Handlers ──────────────────────────────────────────────────────────────────

public sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderDto>
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<GetOrderQueryHandler> _logger;

    public GetOrderQueryHandler(OrdersDbContext db, ILogger<GetOrderQueryHandler> logger)
    {
        _db = db;
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
        _db = db;
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

// ── Tick stream ───────────────────────────────────────────────────────────────

/// <summary>
/// Emits a fixed number of fake <see cref="OrderDto"/> objects at a rate of one
/// per second, then stops.  Useful for observing the Vitreous DevUI stream row
/// update in real time without needing any orders in the database.
/// </summary>
/// <param name="Count">
/// Number of items to emit.  Defaults to 5; clamped to [1, 20].
/// </param>
public sealed record TickStreamQuery(int Count = 5) : IStreamQuery<OrderDto>;

/// <summary>
/// Yields <see cref="TickStreamQuery.Count"/> synthetic orders spaced one second
/// apart so that the DevUI live timer and STREAMING status are clearly visible.
/// </summary>
public sealed class TickStreamQueryHandler(ILogger<TickStreamQueryHandler> logger)
    : StreamQueryHandler<TickStreamQuery, OrderDto>
{
    protected override async IAsyncEnumerable<OrderDto> Stream(
        TickStreamQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var count = Math.Clamp(query.Count, 1, 20);
        logger.LogInformation("Starting tick stream — will emit {Count} item(s) at 1-second intervals.", count);

        for (var i = 1; i <= count; i++)
        {
            ct.ThrowIfCancellationRequested();

            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            logger.LogDebug("Emitting tick item {Index}/{Count}.", i, count);

            yield return new OrderDto(
                Id:           Guid.NewGuid(),
                CustomerName: $"Sample Customer {i}",
                Product:      $"Fake Product {i}",
                Quantity:     i,
                CreatedAt:    DateTimeOffset.UtcNow);
        }

        logger.LogInformation("Tick stream complete — emitted {Count} item(s).", count);
    }
}
