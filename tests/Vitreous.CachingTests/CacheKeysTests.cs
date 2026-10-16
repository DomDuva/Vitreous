using Vitreous.Caching;

namespace Vitreous.CachingTests;

public sealed class CacheKeysTests
{
    [Theory]
    [InlineData("vitreous:", "order:1", "vitreous:order:1")]
    [InlineData("vitreous",  "order:1", "vitreous:order:1")]
    [InlineData("",          "order:1", "order:1")]
    [InlineData(null,        "order:1", "order:1")]
    [InlineData("a:b:",      "k",       "a:b:k")]
    public void Combine_ProducesExactlyOneSeparator(string? prefix, string key, string expected)
        => CacheKeys.Combine(prefix, key).Should().Be(expected);

    [Fact]
    public void Combine_NullKey_Throws()
    {
        var act = () => CacheKeys.Combine("p", null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
