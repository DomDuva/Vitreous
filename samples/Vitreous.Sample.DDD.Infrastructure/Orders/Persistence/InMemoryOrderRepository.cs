using System.Collections.Concurrent;
using Vitreous.Abstractions;
using Vitreous.Sample.DDD.Domain.Orders;

namespace Vitreous.Sample.DDD.Infrastructure.Orders.Persistence;

/// <summary>
/// The orders themselves, shared by every request. A database would play this part in a real app.
/// </summary>
internal sealed class InMemoryOrderStore
{
    public ConcurrentDictionary<Guid, Order> Orders { get; } = new();
}

/// <summary>
/// Implements the domain's <see cref="IOrderRepository"/> port in memory, and publishes each saved
/// order's domain events. It is scoped, because <see cref="IPublisher"/> is scoped.
/// </summary>
internal sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly InMemoryOrderStore _store;
    private readonly IPublisher _publisher;

    public InMemoryOrderRepository(InMemoryOrderStore store, IPublisher publisher)
    {
        _store     = store;
        _publisher = publisher;
    }

    public ValueTask<Order?> FindAsync(Guid id, CancellationToken ct = default) =>
        ValueTask.FromResult(_store.Orders.TryGetValue(id, out var o) ? o : null);

    public ValueTask<IReadOnlyList<Order>> ListAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<Order>>(
            _store.Orders.Values.OrderByDescending(o => o.CreatedAt).ToList());

    public async ValueTask AddAsync(Order order, CancellationToken ct = default)
    {
        _store.Orders[order.Id] = order;

        // Publish only after the order is saved, so handlers never see an order that doesn't exist.
        // Publish dispatches on each event's runtime type, so INotification-typed events reach the
        // handlers of their concrete type.
        foreach (var domainEvent in order.DequeueDomainEvents())
            await _publisher.Publish(domainEvent, ct);
    }

    public ValueTask<bool> RemoveAsync(Guid id, CancellationToken ct = default) =>
        ValueTask.FromResult(_store.Orders.TryRemove(id, out _));
}
