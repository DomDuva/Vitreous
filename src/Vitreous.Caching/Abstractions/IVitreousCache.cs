namespace Vitreous.Caching.Abstractions;

/// <summary>
/// Abstraction over the underlying cache store.
/// Register the default implementation (<c>DistributedVitreousCache</c>) for
/// IDistributedCache-backed caching, or the Redis implementation
/// (<c>RedisVitreousCache</c> from <c>Vitreous.Caching.Redis</c>) for full
/// tag-based invalidation support.
/// </summary>
public interface IVitreousCache
{
    /// <summary>
    /// Attempts to retrieve a value from the cache.
    /// </summary>
    /// <returns>
    /// <c>(true, value)</c> on a hit; <c>(false, default)</c> on a miss or deserialisation error.
    /// </returns>
    ValueTask<(bool Hit, T? Value)> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Stores a value in the cache under <paramref name="key"/>, associated with
    /// <paramref name="tags"/> (used by the Redis backend for bulk invalidation).
    /// </summary>
    ValueTask SetAsync<T>(
        string key,
        T value,
        IReadOnlyList<string> tags,
        DistributedCacheEntryOptions options,
        CancellationToken ct = default);

    /// <summary>Removes a single cache entry by exact key.</summary>
    ValueTask RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes all cache entries associated with <paramref name="tag"/>.
    /// This is a no-op in the base <c>IDistributedCache</c> implementation;
    /// only the Redis backend provides actual tag removal.
    /// </summary>
    ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default);
}
