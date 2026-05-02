namespace Vitreous.Caching.Abstractions;

/// <summary>
/// Marks a query as cacheable. Implement this on any <see cref="IQuery{TResult}"/> to have its
/// result automatically cached by <c>CachingBehavior</c>.
/// </summary>
/// <typeparam name="TResult">The result type returned by the query.</typeparam>
public interface ICacheableQuery<TResult> : IQuery<TResult>
{
    /// <summary>
    /// The unique cache key for this query instance.
    /// Should incorporate any parameters that affect the result, e.g. <c>$"order:{Id}"</c>.
    /// </summary>
    string CacheKey { get; }

    /// <summary>
    /// How long the cached value lives. When set, takes precedence over
    /// <see cref="SlidingExpiration"/>. Null means no absolute expiry.
    /// </summary>
    TimeSpan? AbsoluteExpiration { get; }

    /// <summary>
    /// Sliding window expiration — the entry expires if not accessed within this window.
    /// Ignored by the Redis backend (use <see cref="AbsoluteExpiration"/> instead).
    /// Null means no sliding expiry.
    /// </summary>
    TimeSpan? SlidingExpiration { get; }

    /// <summary>
    /// Optional tags attached to this cache entry.
    /// The base <c>IDistributedCache</c> backend ignores tags.
    /// The Redis backend uses them for tag-based bulk invalidation via <c>IInvalidatesCache</c>.
    /// </summary>
    IReadOnlyList<string> Tags => Array.Empty<string>();
}
