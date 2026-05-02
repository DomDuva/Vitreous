using Vitreous.Abstractions;
using Vitreous.Caching.Abstractions;

namespace Vitreous.Sample.Redis.Orders.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record OrderDto(Guid Id, string CustomerName, string Product, int Quantity, DateTimeOffset CreatedAt);

// ── Queries ───────────────────────────────────────────────────────────────────

/// <summary>
/// Fetches a single order by ID. Results are cached for 5 minutes per order ID.
/// DeleteOrderCommand busts this entry both by direct key and via the "orders" tag.
/// </summary>
public sealed record GetOrderQuery(Guid OrderId) : ICacheableQuery<OrderDto>
{
    public string                CacheKey           => $"order:{OrderId}";
    public TimeSpan?             AbsoluteExpiration => TimeSpan.FromMinutes(5);
    public TimeSpan?             SlidingExpiration  => null;
    // Tagged so DeleteOrderCommand can atomically clear all order-related entries.
    public IReadOnlyList<string> Tags               => ["orders", $"order:{OrderId}"];
}

/// <summary>
/// Returns all orders. Results are cached for 2 minutes.
/// CreateOrderCommand and DeleteOrderCommand both bust this entry via the "orders" tag.
/// </summary>
public sealed record ListOrdersQuery : ICacheableQuery<IReadOnlyList<OrderDto>>
{
    public string                CacheKey           => "orders:list";
    public TimeSpan?             AbsoluteExpiration => TimeSpan.FromMinutes(2);
    public TimeSpan?             SlidingExpiration  => null;
    public IReadOnlyList<string> Tags               => ["orders"];
}

// ── Handlers ──────────────────────────────────────────────────────────────────

public sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderDto>
{
    private readonly OrderRepository _repo;
    private readonly ILogger<GetOrderQueryHandler> _logger;

    public GetOrderQueryHandler(OrderRepository repo, ILogger<GetOrderQueryHandler> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    public ValueTask<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct = default)
    {
        var order = _repo.Find(query.OrderId);

        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} not found.", query.OrderId);
            return ValueTask.FromResult(
                Result<OrderDto>.Fail(Error.NotFound($"Order '{query.OrderId}' not found.")));
        }

        _logger.LogInformation("Fetched order {OrderId}.", order.Id);
        return ValueTask.FromResult(
            Result<OrderDto>.Ok(new OrderDto(order.Id, order.CustomerName, order.Product, order.Quantity, order.CreatedAt)));
    }
}

public sealed class ListOrdersQueryHandler : IQueryHandler<ListOrdersQuery, IReadOnlyList<OrderDto>>
{
    private readonly OrderRepository _repo;
    private readonly ILogger<ListOrdersQueryHandler> _logger;

    public ListOrdersQueryHandler(OrderRepository repo, ILogger<ListOrdersQueryHandler> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    public ValueTask<Result<IReadOnlyList<OrderDto>>> Handle(
        ListOrdersQuery query,
        CancellationToken ct = default)
    {
        var orders = _repo.GetAll()
            .Select(o => new OrderDto(o.Id, o.CustomerName, o.Product, o.Quantity, o.CreatedAt))
            .ToList();

        _logger.LogInformation("Listed {Count} orders.", orders.Count);

        return ValueTask.FromResult(
            Result<IReadOnlyList<OrderDto>>.Ok(orders));
    }
}
