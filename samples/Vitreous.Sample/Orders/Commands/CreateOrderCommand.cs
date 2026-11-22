using Vitreous.Abstractions;

namespace Vitreous.Sample.Orders.Commands;

// ── Command ──────────────────────────────────────────────────────────────────

public sealed record CreateOrderCommand(
    string CustomerName,
    string Product,
    int    Quantity) : ICommand<Guid>;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class CreateOrderCommandHandler
    : ICommandHandler<CreateOrderCommand, Guid>
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(
        OrdersDbContext db,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async ValueTask<Result<Guid>> Handle(
        CreateOrderCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.CustomerName))
            return Error.Validation("CustomerName is required.");

        if (command.Quantity <= 0)
            return Error.Validation("Quantity must be greater than zero.");

        var order = new Order(
            Guid.NewGuid(),
            command.CustomerName,
            command.Product,
            command.Quantity,
            DateTimeOffset.UtcNow);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Order {OrderId} created for customer '{Customer}' — {Qty}x {Product}",
            order.Id, order.CustomerName, order.Quantity, order.Product);

        return order.Id;
    }
}
