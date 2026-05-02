using Vitreous.Abstractions;
using Vitreous.Caching.Abstractions;

namespace Vitreous.Sample.Redis.Orders.Commands;

// ── Command ──────────────────────────────────────────────────────────────────

/// <summary>
/// Deletes an order. On success, removes the order's individual cache entry and
/// busts the "orders" tag (Redis) which covers both the list and any per-order
/// entries still in cache. Returns bool (true) so CacheInvalidationBehavior can
/// observe the Result and trigger invalidation.
/// </summary>
public sealed record DeleteOrderCommand(Guid OrderId) : ICommand<bool>, IInvalidatesCache
{
    // Remove the specific order entry directly — works on all backends.
    IReadOnlyList<string> IInvalidatesCache.CacheKeys       => [$"order:{OrderId}", "orders:list"];

    // Bust the "orders" tag on Redis to atomically clear all related entries.
    IReadOnlyList<string> IInvalidatesCache.InvalidatesTags => ["orders"];
}

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class DeleteOrderCommandHandler : ICommandHandler<DeleteOrderCommand, bool>
{
    private readonly OrderRepository _repo;
    private readonly ILogger<DeleteOrderCommandHandler> _logger;

    public DeleteOrderCommandHandler(
        OrderRepository repo,
        ILogger<DeleteOrderCommandHandler> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    public ValueTask<Result<bool>> Handle(DeleteOrderCommand command, CancellationToken ct = default)
    {
        if (!_repo.Remove(command.OrderId))
        {
            _logger.LogWarning("Attempted to delete non-existent order {OrderId}", command.OrderId);
            return ValueTask.FromResult(Result<bool>.Fail(Error.NotFound(
                $"Order '{command.OrderId}' not found.")));
        }

        _logger.LogInformation("Order {OrderId} deleted.", command.OrderId);
        return ValueTask.FromResult(Result<bool>.Ok(true));
    }
}
