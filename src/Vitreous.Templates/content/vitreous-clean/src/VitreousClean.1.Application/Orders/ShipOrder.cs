using Microsoft.EntityFrameworkCore;
using Vitreous;
using Vitreous.Abstractions;
#if (UseCaching)
using Vitreous.Caching.Abstractions;
#endif
using VitreousClean._1.Application.Abstractions;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Application.Orders;

#if (UseCaching)
// A command without a result: ICommand, and the handler returns Result. IInvalidatesCache evicts
// the cached GetOrderQuery once the command succeeds, so the next read sees the change.
public sealed record ShipOrderCommand(Guid OrderId) : ICommand, IInvalidatesCache
{
    public IReadOnlyList<string> CacheKeys => [GetOrderQuery.CacheKeyFor(OrderId)];
}
#else
// A command without a result: ICommand, and the handler returns Result.
public sealed record ShipOrderCommand(Guid OrderId) : ICommand;
#endif

public sealed class ShipOrderHandler(IAppDbContext db, TimeProvider time) : ICommandHandler<ShipOrderCommand>
{
    public async ValueTask<Result> Handle(ShipOrderCommand command, CancellationToken ct = default)
    {
        var id    = new OrderId(command.OrderId);
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            return Error.NotFound($"Order {id} was not found.");

        var shipped = order.Ship(time.GetUtcNow());
        if (shipped.IsFailure)
            return shipped;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request shipped it since this one read it: Status is a concurrency token.
            return Error.Conflict($"Order {id} was shipped already.");
        }

        return Result.Success;
    }
}
