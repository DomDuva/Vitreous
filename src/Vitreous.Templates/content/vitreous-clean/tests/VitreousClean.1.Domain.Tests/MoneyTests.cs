namespace VitreousClean._1.Domain.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void Two_amounts_with_the_same_value_are_equal()
    {
        Assert.Equal(Money.Create(10m, "EUR").Value, Money.Create(10.00m, "EUR").Value);
        Assert.NotEqual(Money.Create(10m, "EUR").Value, Money.Create(10m, "USD").Value);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.001)]
    [InlineData(1_000_000.01)]
    public void An_invalid_amount_is_rejected(double amount)
    {
        Assert.Equal("VALIDATION", Money.Create((decimal)amount, "EUR").Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("eur")]
    [InlineData("EURO")]
    public void An_invalid_currency_is_rejected(string currency)
    {
        Assert.Equal("VALIDATION", Money.Create(1m, currency).Error.Code);
    }

    [Fact]
    public void Amounts_in_different_currencies_can_not_be_added()
    {
        Assert.Throws<InvalidOperationException>(() => Money.Create(1m, "EUR").Value + Money.Create(1m, "USD").Value);
    }
}
