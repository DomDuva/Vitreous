using Vitreous.Abstractions;

namespace VitreousClean._1.Domain.Orders;

// Domain events are notifications: something that happened, in the past tense. Any number of
// handlers in any layer can react to one, without the domain knowing about them.

public sealed record OrderPlaced(OrderId OrderId, string CustomerEmail, Money Total) : INotification;

public sealed record OrderShipped(OrderId OrderId, string CustomerEmail) : INotification;
