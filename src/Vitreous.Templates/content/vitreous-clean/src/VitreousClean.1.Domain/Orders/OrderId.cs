namespace VitreousClean._1.Domain.Orders;

/// <summary>An order's identity. A type of its own, so no other id can be passed in its place.</summary>
public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
