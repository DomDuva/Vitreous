namespace Vitreous.Caching.Redis.Internal;

/// <summary>
/// <see cref="IVitreousCache"/> implementation backed directly by StackExchange.Redis.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tag-based invalidation</strong> is implemented using Redis Sets.
/// A value and its tag memberships are written atomically by one Lua script: the entry's key
/// is added to a Redis Set named <c>{TagPrefix}{tag}</c> for each tag, and each tag Set's TTL
/// is extended to outlive the longest-lived entry it holds (a Set holding an entry with no TTL
/// never expires). Tag Sets therefore disappear on their own once all their entries have expired.
/// </para>
/// <para>
/// <see cref="RemoveByTagAsync"/> first renames the tag Set atomically, so entries cached while
/// the removal runs go into a fresh Set and are not lost, then walks the renamed Set with
/// <c>SSCAN</c> and deletes its entries with <c>UNLINK</c> in pages. Large tags never block Redis.
/// </para>
/// <para>
/// <strong>Redis Cluster note:</strong> both scripts touch the entry key and its tag keys
/// together, so on Redis Cluster an entry and its tags must share a hash slot (use hash tags,
/// e.g. a key prefix of <c>{myapp}:</c> and a tag prefix of <c>{myapp}:tag:</c>). Standalone
/// Redis and Redis Sentinel have no such restriction.
/// </para>
/// <para>
/// <strong>Sliding expiration</strong> is not supported by Redis directly; when only
/// <see cref="ICacheableQuery{TResult}.SlidingExpiration"/> is set it is applied as a fixed TTL.
/// </para>
/// </remarks>
internal sealed class RedisVitreousCache : IVitreousCache
{
    private const int ScanPageSize = 500;

    private readonly Lazy<IConnectionMultiplexer> _redis;
    private readonly VitreousRedisCachingOptions _options;
    private readonly IVitreousCacheSerializer _serializer;
    private readonly ILogger<RedisVitreousCache> _logger;

    // KEYS[1] = entry key, KEYS[2..n] = tag Set keys. ARGV[1] = value, ARGV[2] = TTL in ms (0 = none).
    private const string SetWithTagsScript = """
        local ttl = tonumber(ARGV[2])
        if ttl > 0 then
            redis.call('SET', KEYS[1], ARGV[1], 'PX', ttl)
        else
            redis.call('SET', KEYS[1], ARGV[1])
        end
        for i = 2, #KEYS do
            local existed = redis.call('EXISTS', KEYS[i])
            redis.call('SADD', KEYS[i], KEYS[1])
            if ttl > 0 then
                local current = redis.call('PTTL', KEYS[i])
                -- A new Set gets the entry's TTL. An existing Set keeps the longer of the two;
                -- PTTL -1 means it already holds an entry without TTL, so it stays persistent.
                if existed == 0 or (current >= 0 and current < ttl) then
                    redis.call('PEXPIRE', KEYS[i], ttl)
                end
            else
                redis.call('PERSIST', KEYS[i])
            end
        end
        return 1
        """;

    // KEYS[1] = tag Set key, KEYS[2] = temporary key to move it to. Returns 1 if moved.
    // The moved Set gets a safety TTL so it cannot outlive a process that dies mid-removal.
    private const string DetachTagScript = """
        if redis.call('EXISTS', KEYS[1]) == 1 then
            redis.call('RENAME', KEYS[1], KEYS[2])
            if redis.call('PTTL', KEYS[2]) < 0 then
                redis.call('PEXPIRE', KEYS[2], 3600000)
            end
            return 1
        end
        return 0
        """;

    public RedisVitreousCache(
        Func<IConnectionMultiplexer> redis,
        VitreousRedisCachingOptions options,
        IVitreousCacheSerializer serializer,
        ILogger<RedisVitreousCache> logger)
    {
        // PublicationOnly: a failed resolution is retried on the next call instead of being
        // cached forever, so the cache recovers once a connection becomes available.
        _redis      = new Lazy<IConnectionMultiplexer>(redis, LazyThreadSafetyMode.PublicationOnly);
        _options    = options;
        _serializer = serializer;
        _logger     = logger;
    }

    private IDatabase Database => _redis.Value.GetDatabase();

    public async ValueTask<(bool Hit, T? Value)> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var raw = await Database.StringGetAsync(PrefixKey(key));

        if (raw.IsNullOrEmpty)
            return (false, default);

        try
        {
            return _serializer.TryDeserialize<T>((byte[])raw!, out var value)
                ? (true, value)
                : (false, default);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
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
        var bytes = _serializer.Serialize(value);
        var ttl = options.AbsoluteExpirationRelativeToNow ?? options.SlidingExpiration;

        var keys = new RedisKey[tags.Count + 1];
        keys[0] = PrefixKey(key);
        for (var i = 0; i < tags.Count; i++)
            keys[i + 1] = PrefixTag(tags[i]);

        var ttlMs = ttl.HasValue ? Math.Max(1L, (long)ttl.Value.TotalMilliseconds) : 0L;

        await Database.ScriptEvaluateAsync(SetWithTagsScript, keys, [bytes, ttlMs]);
    }

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
        => await Database.KeyDeleteAsync(PrefixKey(key));

    public async ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        var db = Database;
        var tagKey = PrefixTag(tag);
        var detachedKey = (RedisKey)(tagKey + ":purge:" + Guid.NewGuid().ToString("N"));

        var detached = (int)await db.ScriptEvaluateAsync(DetachTagScript, [tagKey, detachedKey]);
        if (detached == 0)
            return;

        var removed = 0;
        try
        {
            var page = new List<RedisKey>(ScanPageSize);
            await foreach (var member in db.SetScanAsync(detachedKey, pageSize: ScanPageSize).WithCancellation(ct))
            {
                page.Add((string)member!);
                if (page.Count == ScanPageSize)
                {
                    removed += await UnlinkAsync(db, page);
                    page.Clear();
                }
            }

            if (page.Count > 0)
                removed += await UnlinkAsync(db, page);
        }
        finally
        {
            await db.ExecuteAsync("UNLINK", detachedKey);
        }

        _logger.LogDebug(
            "Vitreous.Caching.Redis: removed {Count} entries for tag '{Tag}'", removed, tag);
    }

    private static async Task<int> UnlinkAsync(IDatabase db, List<RedisKey> keys) =>
        (int)await db.ExecuteAsync("UNLINK", keys.Cast<object>().ToArray());

    private string PrefixKey(string key) => CacheKeys.Combine(_options.KeyPrefix, key);

    private string PrefixTag(string tag) => CacheKeys.Combine(_options.TagPrefix, tag);
}
