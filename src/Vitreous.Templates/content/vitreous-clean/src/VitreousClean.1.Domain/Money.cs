using Vitreous;

namespace VitreousClean._1.Domain;

/// <summary>
/// A value object: it has no identity, two of them with the same amount and currency are equal,
/// and it can't change. The private constructor makes <see cref="Create"/> the only way to get
/// one, so a Money that exists is valid.
/// </summary>
public sealed record Money
{
    public const decimal MaxAmount = 1_000_000;

    private Money(decimal amount, string currency)
    {
        Amount   = amount;
        Currency = currency;
    }

    public decimal Amount   { get; }
    public string  Currency { get; }

    /// <summary>Returns <see cref="Error.Validation"/> when the amount or the currency is invalid.</summary>
    public static Result<Money> Create(decimal amount, string currency)
    {
        var failures = new Dictionary<string, string[]>();

        if (amount is < 0 or > MaxAmount || decimal.Round(amount, 2) != amount)
            failures[nameof(Amount)] = [$"An amount must be between 0 and {MaxAmount:0}, with at most two decimals."];
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
            failures[nameof(Currency)] = ["A currency is a three-letter code, such as EUR."];

        if (failures.Count > 0)
            return Error.Validation("The amount is invalid.", failures);

        return new Money(amount, currency);
    }

    public static Money operator *(Money price, int quantity) => new(price.Amount * quantity, price.Currency);

    /// <summary>Adding two currencies is a bug in the caller, not an expected failure, so it throws.</summary>
    public static Money operator +(Money left, Money right) => left.Currency == right.Currency
        ? new(left.Amount + right.Amount, left.Currency)
        : throw new InvalidOperationException($"Can't add {right.Currency} to {left.Currency}.");

    public override string ToString() => FormattableString.Invariant($"{Amount:0.00} {Currency}");
}
