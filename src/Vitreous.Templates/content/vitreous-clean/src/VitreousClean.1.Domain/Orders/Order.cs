using Vitreous;
using Vitreous.Abstractions;

namespace VitreousClean._1.Domain.Orders;

/// <summary>
/// The order aggregate. Its state only changes through its methods, and each one checks the
/// business rules first, so an order that breaks one can't exist, whoever calls it. A broken rule
/// is an expected failure: it is returned as an <see cref="Error"/>, not thrown.
/// </summary>
public sealed class Order : IHasDomainEvents
{
    public const int MaxCustomerEmailLength = 320;
    public const int MaxLines               = 50;

    private readonly List<OrderLine>     _lines        = [];
    private readonly List<INotification> _domainEvents = [];

    // EF Core calls this constructor too, and fills the lines and the properties with a setter afterwards.
    private Order(OrderId id, string customerEmail, DateTimeOffset placedAt)
    {
        Id            = id;
        CustomerEmail = customerEmail;
        PlacedAt      = placedAt;
    }

    public OrderId         Id            { get; }
    public string          CustomerEmail { get; }
    public DateTimeOffset  PlacedAt      { get; }
    public OrderStatus     Status        { get; private set; }
    public DateTimeOffset? ShippedAt     { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public Money Total => _lines.Select(static l => l.Total).Aggregate(static (sum, line) => sum + line);

    /// <summary>Places an order and records <see cref="OrderPlaced"/>.</summary>
    public static Result<Order> Place(string customerEmail, IReadOnlyCollection<OrderLine> lines, DateTimeOffset placedAt)
    {
        var failures = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(customerEmail) || customerEmail.Length > MaxCustomerEmailLength)
            failures[nameof(CustomerEmail)] = [$"An order needs a customer email of at most {MaxCustomerEmailLength} characters."];

        if (lines.Count is 0 or > MaxLines)
            failures[nameof(Lines)] = [$"An order needs between 1 and {MaxLines} lines."];
        else if (lines.Select(static l => l.UnitPrice.Currency).Distinct().Count() > 1)
            failures[nameof(Lines)] = ["The lines of an order must share one currency."];

        if (failures.Count > 0)
            return Error.Validation("The order is invalid.", failures);

        var order = new Order(OrderId.New(), customerEmail, placedAt);
        order._lines.AddRange(lines);
        order._domainEvents.Add(new OrderPlaced(order.Id, order.CustomerEmail, order.Total));
        return order;
    }

    /// <summary>Ships the order and records <see cref="OrderShipped"/>. An order ships only once.</summary>
    public Result Ship(DateTimeOffset shippedAt)
    {
        if (Status != OrderStatus.Placed)
            return Error.Conflict($"Order {Id} was shipped already.");

        Status    = OrderStatus.Shipped;
        ShippedAt = shippedAt;
        _domainEvents.Add(new OrderShipped(Id, CustomerEmail));
        return Result.Success;
    }

    /// <inheritdoc/>
    public IReadOnlyList<INotification> DequeueDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}
