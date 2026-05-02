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
    private readonly OrderRepository _repo;
    private readonly ILogger<DeleteOrderCommandHandler> _logger;

    public DeleteOrderCommandHandler(
        OrderRepository repo,
        ILogger<DeleteOrderCommandHandler> logger)
    {
        _repo = repo;
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
