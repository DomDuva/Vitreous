namespace Vitreous.Sample.DDD.Application.Orders;

/// <summary>An in-memory order, used instead of a real database for simplicity.</summary>
public sealed record Order(Guid Id, string CustomerName, string Product, int Quantity, DateTimeOffset CreatedAt);
