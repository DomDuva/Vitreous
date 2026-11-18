using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Vitreous.Caching.Redis;

namespace Vitreous.CachingTests.Redis;

/// <summary>
/// Exercises the Redis backend against a real server (see <see cref="RedisFactAttribute"/>).
/// Each test uses its own key and tag prefixes so tests never see each other's data.
/// </summary>
public sealed class RedisVitreousCacheTests : IDisposable
{
    private readonly string _keyPrefix = $"vt-{Guid.NewGuid():N}:";
    private readonly string _tagPrefix = $"vtt-{Guid.NewGuid():N}:";
    private readonly ServiceProvider? _services;
    private readonly IConnectionMultiplexer? _redis;

    public RedisVitreousCacheTests()
    {
        if (RedisFactAttribute.ConnectionString is not { Length: > 0 } connectionString)
            return;

        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddVitreousRedisCaching(o =>
        {
            o.ConnectionString = connectionString;
            o.KeyPrefix        = _keyPrefix;
            o.TagPrefix        = _tagPrefix;
        });
        _services = services.BuildServiceProvider();
        _redis    = _services.GetRequiredService<IConnectionMultiplexer>();
    }

    private IVitreousCache Cache => _services!.GetRequiredService<IVitreousCache>();

    private IDatabase Db => _redis!.GetDatabase();

    private static DistributedCacheEntryOptions Ttl(TimeSpan? ttl) => new() { AbsoluteExpirationRelativeToNow = ttl };

    [RedisFact]
    public async Task SetThenGet_RoundTrips_AndAppliesTheTtl()
    {
        await Cache.SetAsync("order:1", "value", [], Ttl(TimeSpan.FromMinutes(1)));

        (await Cache.GetAsync<string>("order:1")).Should().Be((true, "value"));
        (await Db.KeyTimeToLiveAsync(_keyPrefix + "order:1")).Should().BeCloseTo(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
    }

    [RedisFact]
    public async Task ValueWrittenForAnotherType_IsAMiss()
    {
        await Cache.SetAsync("shared", 42, [], Ttl(TimeSpan.FromMinutes(1)));

        (await Cache.GetAsync<string>("shared")).Hit.Should().BeFalse();
    }

    [RedisFact]
    public async Task ValueInTheOldUnwrappedFormat_IsAMiss()
    {
        await Db.StringSetAsync(_keyPrefix + "legacy", "\"plain\"");

        (await Cache.GetAsync<string>("legacy")).Hit.Should().BeFalse();
    }

    [RedisFact]
    public async Task TagSet_ExpiresWithItsLongestLivedEntry()
    {
        await Cache.SetAsync("a", "a", ["orders"], Ttl(TimeSpan.FromMinutes(1)));
        var afterFirst = await Db.KeyTimeToLiveAsync(_tagPrefix + "orders");

        await Cache.SetAsync("b", "b", ["orders"], Ttl(TimeSpan.FromMinutes(10)));
        var afterLonger = await Db.KeyTimeToLiveAsync(_tagPrefix + "orders");

        await Cache.SetAsync("c", "c", ["orders"], Ttl(TimeSpan.FromSeconds(30)));
        var afterShorter = await Db.KeyTimeToLiveAsync(_tagPrefix + "orders");

        afterFirst.Should().BeCloseTo(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
        afterLonger.Should().BeCloseTo(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5));
        afterShorter.Should().BeCloseTo(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5), "a shorter entry must not shorten the Set");
        (await Db.SetLengthAsync(_tagPrefix + "orders")).Should().Be(3);
    }

    [RedisFact]
    public async Task TagSet_HoldingAnEntryWithoutTtl_NeverExpires()
    {
        await Cache.SetAsync("a", "a", ["orders"], Ttl(TimeSpan.FromMinutes(1)));
        await Cache.SetAsync("b", "b", ["orders"], Ttl(null));
        await Cache.SetAsync("c", "c", ["orders"], Ttl(TimeSpan.FromMinutes(1)));

        (await Db.KeyTimeToLiveAsync(_tagPrefix + "orders")).Should().BeNull();
    }

    [RedisFact]
    public async Task RemoveByTag_RemovesEveryTaggedEntry_AcrossScanPages()
    {
        const int count = 1_200; // more than one SSCAN/UNLINK page
        for (var i = 0; i < count; i++)
            await Cache.SetAsync($"order:{i}", i, ["orders"], Ttl(TimeSpan.FromMinutes(1)));
        await Cache.SetAsync("untagged", "keep", [], Ttl(TimeSpan.FromMinutes(1)));
        await Cache.SetAsync("other", "keep", ["customers"], Ttl(TimeSpan.FromMinutes(1)));

        await Cache.RemoveByTagAsync("orders");

        for (var i = 0; i < count; i += 97)
            (await Cache.GetAsync<int>($"order:{i}")).Hit.Should().BeFalse();
        (await Db.KeyExistsAsync(_tagPrefix + "orders")).Should().BeFalse();
        (await Cache.GetAsync<string>("untagged")).Hit.Should().BeTrue();
        (await Cache.GetAsync<string>("other")).Hit.Should().BeTrue();
        (await Db.ExecuteAsync("KEYS", _tagPrefix + "orders:purge:*")).Length.Should().Be(0,
            "the detached Set is deleted after the removal");
    }

    [RedisFact]
    public async Task RemoveByTag_UnknownTag_IsANoOp()
    {
        await Cache.RemoveByTagAsync("missing");

        (await Db.KeyExistsAsync(_tagPrefix + "missing")).Should().BeFalse();
    }

    public void Dispose()
    {
        if (_redis is null)
            return;

        // Best-effort cleanup of everything this test wrote.
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        foreach (var pattern in new[] { _keyPrefix + "*", _tagPrefix + "*" })
        {
            var keys = server.Keys(pattern: pattern).ToArray();
            if (keys.Length > 0)
                _redis.GetDatabase().KeyDelete(keys);
        }

        _services!.Dispose();
    }
}
