namespace Vitreous.Caching.Redis.Internal;

/// <summary>
/// <see cref="IVitreousCache"/> implementation backed directly by StackExchange.Redis.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tag-based invalidation</strong> is implemented using Redis Sets.
/// When a cache entry is stored with one or more tags, the entry's key is added to a
/// Redis Set named <c>{TagPrefix}{tag}</c> for each tag. When
/// <see cref="RemoveByTagAsync"/> is called, a Lua script atomically reads all keys
/// in that Set, deletes them, and then deletes the Set itself.
/// </para>
/// <para>
/// <strong>Redis Cluster note:</strong> the Lua script used for tag invalidation
/// DELetes keys that are determined at runtime (not declared in <c>KEYS[]</c>).
/// This works correctly on standalone Redis and Redis Sentinel, but is not compatible
/// with Redis Cluster if the tagged keys reside on different slots. For Cluster
/// deployments, ensure all keys sharing a tag use the same hash slot (e.g. use hash
/// tags: <c>{order}:123</c>).
/// </para>
/// <para>
/// <strong>Sliding expiration</strong> is not supported; set
/// <see cref="ICacheableQuery{TResult}.AbsoluteExpiration"/> instead.
/// </para>
/// </remarks>
internal sealed class RedisVitreousCache : IVitreousCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly VitreousRedisCachingOptions _options;
    private readonly ILogger<RedisVitreousCache> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Lua script: atomically remove all keys associated with a tag and the tag Set itself.
    // KEYS[1] = the tag Set key (e.g. "vtag:orders")
    private const string RemoveByTagScript = """
        local members = redis.call('SMEMBERS', KEYS[1])
        for _, k in ipairs(members) do
            redis.call('DEL', k)
        end
        redis.call('DEL', KEYS[1])
        return #members
        """;

    public RedisVitreousCache(
        IConnectionMultiplexer redis,
        VitreousRedisCachingOptions options,
        ILogger<RedisVitreousCache> logger)
    {
        _redis = redis;
        _options = options;
        _logger = logger;
    }

    public async ValueTask<(bool Hit, T? Value)> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var raw = await db.StringGetAsync(PrefixKey(key));

        if (raw.IsNullOrEmpty)
            return (false, default);

        try
        {
            var value = JsonSerializer.Deserialize<T>((string)raw!, _json);
            return (true, value);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Vitreous.Caching.Redis: failed to deserialise value for key '{Key}'", key);
            return (false, default);
        }
    }

    public async ValueTask SetAsync<T>(
        string key,
        T value,
        IReadOnlyList<string> tags,
        DistributedCacheEntryOptions options,
        CancellationToken ct = default)
    {
        var prefixedKey = PrefixKey(key);
        var json = JsonSerializer.Serialize(value, _json);
        var ttl = options.AbsoluteExpirationRelativeToNow ?? options.SlidingExpiration;

        var db = _redis.GetDatabase();

        // Write the value
        if (ttl.HasValue)
            await db.StringSetAsync(prefixedKey, json, ttl.Value);
        else
            await db.StringSetAsync(prefixedKey, json);

        // Register the key under each tag Set so it can be bulk-removed later
        if (tags.Count > 0)
        {
            var batch = db.CreateBatch();
            var pending = new List<Task>(tags.Count);

            foreach (var tag in tags)
                pending.Add(batch.SetAddAsync(PrefixTag(tag), prefixedKey));

            batch.Execute();
            await Task.WhenAll(pending);
        }
    }

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
        => await _redis.GetDatabase().KeyDeleteAsync(PrefixKey(key));

    public async ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var tagKey = PrefixTag(tag);
        var removed = (int)await db.ScriptEvaluateAsync(RemoveByTagScript, new RedisKey[] { tagKey });

        _logger.LogDebug(
            "Vitreous.Caching.Redis: removed {Count} entries for tag '{Tag}'", removed, tag);
    }

    private string PrefixKey(string key) =>
        string.IsNullOrEmpty(_options.KeyPrefix) ? key : _options.KeyPrefix + key;

    private string PrefixTag(string tag) =>
        string.IsNullOrEmpty(_options.TagPrefix) ? tag : _options.TagPrefix + tag;
}
