using Vitreous.Abstractions;

namespace Vitreous.Sample.DDD.Domain.Orders;

/// <summary>
/// The order aggregate. It can only be created through <see cref="Create"/>, which enforces the
/// invariants and records an <see cref="OrderCreated"/> domain event.
/// </summary>
public sealed class Order
{
    /// <summary>The most units a single order may contain.</summary>
    public const int MaxQuantity = 100;

    private readonly List<INotification> _domainEvents = [];

    private Order(Guid id, string customerName, string product, int quantity, DateTimeOffset createdAt)
    {
        Id           = id;
        CustomerName = customerName;
        Product      = product;
        Quantity     = quantity;
        CreatedAt    = createdAt;
    }

    public Guid           Id           { get; }
    public string         CustomerName { get; }
    public string         Product      { get; }
    public int            Quantity     { get; }
    public DateTimeOffset CreatedAt    { get; }

    /// <summary>
    /// Creates an order, or returns <see cref="Error.Validation"/> when an invariant is broken.
    /// <para>
    /// The application layer validates input before it gets here, but the aggregate does not rely
    /// on that: an order that breaks an invariant can't be created, whoever calls this.
    /// <see cref="MaxQuantity"/> is a business rule that only the domain knows about.
    /// </para>
    /// </summary>
    public static Result<Order> Create(string customerName, string product, int quantity, DateTimeOffset createdAt)
    {
        var failures = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(customerName))
            failures[nameof(CustomerName)] = ["CustomerName is required."];
        if (string.IsNullOrWhiteSpace(product))
            failures[nameof(Product)] = ["Product is required."];
        if (quantity is <= 0 or > MaxQuantity)
            failures[nameof(Quantity)] = [$"Quantity must be between 1 and {MaxQuantity}."];

        if (failures.Count > 0)
            return Error.Validation("The order is invalid.", failures);

        var order = new Order(Guid.NewGuid(), customerName, product, quantity, createdAt);
        order._domainEvents.Add(new OrderCreated(order.Id, order.CustomerName, order.Product));
        return order;
    }

    /// <summary>
    /// Returns the domain events recorded since the last call and clears them, so each event is
    /// published once. The repository calls this after it saves the order.
    /// </summary>
    public IReadOnlyList<INotification> DequeueDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}
