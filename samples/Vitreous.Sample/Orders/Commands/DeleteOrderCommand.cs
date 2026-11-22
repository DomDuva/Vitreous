using Microsoft.EntityFrameworkCore;
using Vitreous.Abstractions;

namespace Vitreous.Sample.Orders.Commands;

// ── Command ──────────────────────────────────────────────────────────────────

public sealed record DeleteOrderCommand(Guid OrderId) : ICommand;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class DeleteOrderCommandHandler : ICommandHandler<DeleteOrderCommand>
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<DeleteOrderCommandHandler> _logger;

    public DeleteOrderCommandHandler(
        OrdersDbContext db,
        ILogger<DeleteOrderCommandHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async ValueTask<Result> Handle(DeleteOrderCommand command, CancellationToken ct = default)
    {
        var deleted = await _db.Orders
            .Where(o => o.Id == command.OrderId)
            .ExecuteDeleteAsync(ct);

        if (deleted == 0)
        {
            _logger.LogWarning("Attempted to delete non-existent order {OrderId}", command.OrderId);
            return Result.Fail(Error.NotFound($"Order '{command.OrderId}' not found."));
        }

        _logger.LogInformation("Order {OrderId} deleted.", command.OrderId);
        return Result.Success;
    }
}
