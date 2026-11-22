using Microsoft.EntityFrameworkCore;
using Vitreous.Abstractions;
using Vitreous.Caching.Abstractions;

namespace Vitreous.Sample.Caching.Orders.Commands;

// ── Command ──────────────────────────────────────────────────────────────────

/// <summary>
/// Deletes an order. On success, removes the order's individual cache entry and
/// the list cache entry. Returns bool (true) so CacheInvalidationBehavior can
/// observe the Result and trigger invalidation.
/// </summary>
public sealed record DeleteOrderCommand(Guid OrderId) : ICommand<bool>, IInvalidatesCache
{
    IReadOnlyList<string> IInvalidatesCache.CacheKeys => [$"order:{OrderId}", "orders:list"];
}

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class DeleteOrderCommandHandler : ICommandHandler<DeleteOrderCommand, bool>
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<DeleteOrderCommandHandler> _logger;

    public DeleteOrderCommandHandler(
        OrdersDbContext db,
        ILogger<DeleteOrderCommandHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async ValueTask<Result<bool>> Handle(DeleteOrderCommand command, CancellationToken ct = default)
    {
        var deleted = await _db.Orders
            .Where(o => o.Id == command.OrderId)
            .ExecuteDeleteAsync(ct);

        if (deleted == 0)
        {
            _logger.LogWarning("Attempted to delete non-existent order {OrderId}", command.OrderId);
            return Error.NotFound($"Order '{command.OrderId}' not found.");
        }

        _logger.LogInformation("Order {OrderId} deleted.", command.OrderId);
        return true;
    }
}
