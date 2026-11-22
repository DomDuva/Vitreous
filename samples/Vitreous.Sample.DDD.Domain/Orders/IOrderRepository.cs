namespace Vitreous.Sample.DDD.Domain.Orders;

/// <summary>
/// Persistence port for <see cref="Order"/>. The domain declares it and the infrastructure layer
/// implements it, so the domain never depends on storage.
/// </summary>
public interface IOrderRepository
{
    ValueTask<Order?> FindAsync(Guid id, CancellationToken ct = default);

    ValueTask<IReadOnlyList<Order>> ListAsync(CancellationToken ct = default);

    /// <summary>Saves the order, then publishes the domain events it recorded.</summary>
    ValueTask AddAsync(Order order, CancellationToken ct = default);

    ValueTask<bool> RemoveAsync(Guid id, CancellationToken ct = default);
}
