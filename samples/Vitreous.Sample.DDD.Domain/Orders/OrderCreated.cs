using Vitreous.Abstractions;

namespace Vitreous.Sample.DDD.Domain.Orders;

/// <summary>
/// Domain event raised by <see cref="Order.Create"/>. It is an <see cref="INotification"/>, so any
/// number of handlers in any layer can react to it without the domain knowing about them.
/// </summary>
public sealed record OrderCreated(Guid OrderId, string CustomerName, string Product) : INotification;
