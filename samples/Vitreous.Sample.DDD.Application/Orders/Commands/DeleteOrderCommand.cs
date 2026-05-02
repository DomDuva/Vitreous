using Microsoft.Extensions.Logging;
using Vitreous.Abstractions;
using Vitreous.Sample.DDD.Application.Orders;

namespace Vitreous.Sample.DDD.Application.Orders.Commands;

// ── Command ──────────────────────────────────────────────────────────────────

public sealed record DeleteOrderCommand(Guid OrderId) : ICommand;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class DeleteOrderCommandHandler : ICommandHandler<DeleteOrderCommand>
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

    public ValueTask<Result> Handle(DeleteOrderCommand command, CancellationToken ct = default)
    {
        if (!_repo.Remove(command.OrderId))
        {
            _logger.LogWarning("Attempted to delete non-existent order {OrderId}", command.OrderId);
            return ValueTask.FromResult(Result.Fail(Error.NotFound(
                $"Order '{command.OrderId}' not found.")));
        }

        _logger.LogInformation("Order {OrderId} deleted.", command.OrderId);
        return ValueTask.FromResult(Result.Success);
    }
}
