namespace Vitreous.Sample.Caching.Orders;

/// <summary>An order, stored through <see cref="OrdersDbContext"/>.</summary>
public sealed record Order(Guid Id, string CustomerName, string Product, int Quantity, DateTimeOffset CreatedAt);
