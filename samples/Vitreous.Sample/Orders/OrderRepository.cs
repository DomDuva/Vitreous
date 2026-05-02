using System.Collections.Concurrent;

namespace Vitreous.Sample.Orders;

/// <summary>
/// Simple in-memory order repository — shared singleton across the sample.
/// </summary>
public sealed class OrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Order? Find(Guid id) =>
        _orders.TryGetValue(id, out var o) ? o : null;

    public IReadOnlyList<Order> GetAll() =>
        _orders.Values.OrderByDescending(o => o.CreatedAt).ToList();

    public Order Add(Order order)
    {
        _orders[order.Id] = order;
        return order;
    }

    public bool Remove(Guid id) => _orders.TryRemove(id, out _);
}
