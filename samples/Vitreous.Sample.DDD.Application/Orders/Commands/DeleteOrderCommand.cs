using Microsoft.Extensions.Logging;
using Vitreous.Abstractions;
using Vitreous.Sample.DDD.Domain.Orders;

namespace Vitreous.Sample.DDD.Application.Orders.Commands;

// ── Command ──────────────────────────────────────────────────────────────────

public sealed record DeleteOrderCommand(Guid OrderId) : ICommand;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class DeleteOrderCommandHandler : ICommandHandler<DeleteOrderCommand>
{
    private readonly IOrderRepository _repo;
    private readonly ILogger<DeleteOrderCommandHandler> _logger;

    public DeleteOrderCommandHandler(
        IOrderRepository repo,
        ILogger<DeleteOrderCommandHandler> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    public async ValueTask<Result> Handle(DeleteOrderCommand command, CancellationToken ct = default)
    {
        if (!await _repo.RemoveAsync(command.OrderId, ct))
        {
            _logger.LogWarning("Attempted to delete non-existent order {OrderId}", command.OrderId);
            return Result.Fail(Error.NotFound($"Order '{command.OrderId}' not found."));
        }

        _logger.LogInformation("Order {OrderId} deleted.", command.OrderId);
        return Result.Success;
    }
}
