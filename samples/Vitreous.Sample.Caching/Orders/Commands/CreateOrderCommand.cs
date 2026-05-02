using Vitreous.Abstractions;
using Vitreous.Caching.Abstractions;

namespace Vitreous.Sample.Caching.Orders.Commands;

// ── Command ──────────────────────────────────────────────────────────────────

/// <summary>
/// Creates an order. On success, busts the "orders:list" cache key so the
/// next ListOrdersQuery sees the new order.
/// </summary>
public sealed record CreateOrderCommand(
    string CustomerName,
    string Product,
    int Quantity) : ICommand<Guid>, IInvalidatesCache
{
    IReadOnlyList<string> IInvalidatesCache.CacheKeys => ["orders:list"];
}

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class CreateOrderCommandHandler
    : ICommandHandler<CreateOrderCommand, Guid>
{
    private readonly OrderRepository _repo;
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(
        OrderRepository repo,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public ValueTask<Result<Guid>> Handle(
        CreateOrderCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.CustomerName))
            return ValueTask.FromResult(
                Result<Guid>.Fail(Error.Validation("CustomerName is required.")));

        if (command.Quantity <= 0)
            return ValueTask.FromResult(
                Result<Guid>.Fail(Error.Validation("Quantity must be greater than zero.")));

        var order = new Order(
            Guid.NewGuid(),
            command.CustomerName,
            command.Product,
            command.Quantity,
            DateTimeOffset.UtcNow);

        _repo.Add(order);

        _logger.LogInformation(
            "Order {OrderId} created for customer '{Customer}' — {Qty}x {Product}",
            order.Id, order.CustomerName, order.Quantity, order.Product);

        return ValueTask.FromResult(Result<Guid>.Ok(order.Id));
    }
}
