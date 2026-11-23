using Vitreous;

namespace VitreousClean._1.Domain.Orders;

/// <summary>Part of the <see cref="Order"/> aggregate: it is loaded and saved with its order.</summary>
public sealed class OrderLine
{
    public const int MaxProductLength = 200;
    public const int MaxQuantity      = 1000;

    // EF Core calls this constructor too, and sets UnitPrice afterwards.
    private OrderLine(string product, int quantity)
    {
        Product  = product;
        Quantity = quantity;
    }

    public string Product   { get; }
    public int    Quantity  { get; }
    public Money  UnitPrice { get; private init; } = null!;

    public Money Total => UnitPrice * Quantity;

    public static Result<OrderLine> Create(string product, int quantity, Money unitPrice)
    {
        var failures = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(product) || product.Length > MaxProductLength)
            failures[nameof(Product)] = [$"A line needs a product of at most {MaxProductLength} characters."];
        if (quantity is < 1 or > MaxQuantity)
            failures[nameof(Quantity)] = [$"A line needs a quantity between 1 and {MaxQuantity}."];

        if (failures.Count > 0)
            return Error.Validation("The order line is invalid.", failures);

        return new OrderLine(product.Trim(), quantity) { UnitPrice = unitPrice };
    }
}
