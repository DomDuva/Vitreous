namespace Vitreous.Caching.Internal;

/// <summary>
/// Default implementation of <see cref="IVitreousCache"/> backed by
/// <see cref="IDistributedCache"/>. Serialises values as JSON.
/// <para>
/// Tag-based removal (<see cref="RemoveByTagAsync"/>) is a <em>no-op</em> with this
/// implementation. Use <c>Vitreous.Caching.Redis</c> if you need tag invalidation.
/// </para>
/// </summary>
internal sealed class DistributedVitreousCache : IVitreousCache
{
    private readonly IDistributedCache _cache;
    private readonly string _keyPrefix;
    private readonly ILogger<DistributedVitreousCache> _logger;
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DistributedVitreousCache(
        IDistributedCache cache,
        VitreousCachingOptions options,
        ILogger<DistributedVitreousCache> logger)
    {
        _cache     = cache;
        _keyPrefix = options.KeyPrefix;
        _logger    = logger;
    }

    public async ValueTask<(bool Hit, T? Value)> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var bytes = await _cache.GetAsync(Prefix(key), ct);
        if (bytes is null)
            return (false, default);

        try
        {
            var value = JsonSerializer.Deserialize<T>(bytes, _json);
            return (true, value);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Vitreous.Caching: failed to deserialise cached value for key '{Key}'", key);
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
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _json);
        await _cache.SetAsync(Prefix(key), bytes, options, ct);
        // Tags are not tracked by this backend — use Vitreous.Caching.Redis for tag invalidation.
    }

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
        => await _cache.RemoveAsync(Prefix(key), ct);

    /// <inheritdoc/>
    /// <remarks>This is a no-op with the <see cref="IDistributedCache"/> backend.</remarks>
    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    private string Prefix(string key) => string.IsNullOrEmpty(_keyPrefix) ? key : _keyPrefix + ":" + key;
}
