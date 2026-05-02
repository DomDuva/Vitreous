namespace Vitreous.Caching.Abstractions;

/// <summary>
/// Marks a command (or query) as one that invalidates cache entries after it succeeds.
/// Implement this on any <see cref="ICommand{TResult}"/> or <see cref="ICommand"/> to have
/// cache entries removed automatically by <c>CacheInvalidationBehavior</c>.
/// </summary>
/// <remarks>
/// Cache entries are only invalidated when the handler returns a successful <see cref="Result{T}"/>.
/// If the handler fails, no cache keys are touched.
/// </remarks>
public interface IInvalidatesCache
{
    /// <summary>
    /// Exact cache keys to remove from the store.
    /// Works with all backends (IDistributedCache and Redis).
    /// </summary>
    IReadOnlyList<string> CacheKeys => Array.Empty<string>();

    /// <summary>
    /// Tags whose associated cache entries will be bulk-removed.
    /// Only honoured by the Redis backend (<c>Vitreous.Caching.Redis</c>).
    /// The base <c>IDistributedCache</c> backend silently ignores this.
    /// </summary>
    IReadOnlyList<string> InvalidatesTags => Array.Empty<string>();
}
