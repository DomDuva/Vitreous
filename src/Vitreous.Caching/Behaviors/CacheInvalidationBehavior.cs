namespace Vitreous.Caching.Behaviors;

/// <summary>
/// Pipeline behavior that removes stale cache entries after a successful command.
/// Only active for messages that implement <see cref="IInvalidatesCache"/>;
/// all other messages pass through unchanged.
/// </summary>
/// <remarks>
/// Cache entries are removed <em>only</em> when the handler returns a successful
/// <see cref="Result{T}"/>. On failure, no cache state is modified.
/// <para>
/// <see cref="IInvalidatesCache.CacheKeys"/> are honoured by all backends.
/// <see cref="IInvalidatesCache.InvalidatesTags"/> are a no-op with the base
/// <c>IDistributedCache</c> backend and require the <c>Vitreous.Caching.Redis</c>
/// package for actual tag-based bulk removal.
/// </para>
/// Removal failures are logged as warnings and do not affect the command's result —
/// the cache may temporarily be stale, but the command succeeded.
/// </remarks>
public sealed class CacheInvalidationBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
{
    private readonly IVitreousCache _cache;
    private readonly ILogger<CacheInvalidationBehavior<TMessage, TResult>> _logger;

    public CacheInvalidationBehavior(
        IVitreousCache cache,
        ILogger<CacheInvalidationBehavior<TMessage, TResult>> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    public async ValueTask<Result<TResult>> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        if (message is not IInvalidatesCache invalidator)
            return await next(message, ct);

        var result = await next(message, ct);

        if (!result.IsSuccess)
            return result;

        // ── Remove direct keys ─────────────────────────────────────────────────
        foreach (var key in invalidator.CacheKeys)
        {
            try
            {
                await _cache.RemoveAsync(key, ct);
                _logger.LogDebug(
                    "Vitreous.Caching: invalidated key='{Key}' after {MessageType}",
                    key, typeof(TMessage).Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Vitreous.Caching: failed to remove key='{Key}' after {MessageType}",
                    key, typeof(TMessage).Name);
            }
        }

        // ── Remove by tag (no-op with IDistributedCache backend) ─────────────
        foreach (var tag in invalidator.InvalidatesTags)
        {
            try
            {
                await _cache.RemoveByTagAsync(tag, ct);
                _logger.LogDebug(
                    "Vitreous.Caching: invalidated tag='{Tag}' after {MessageType}",
                    tag, typeof(TMessage).Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Vitreous.Caching: failed to remove tag='{Tag}' after {MessageType}",
                    tag, typeof(TMessage).Name);
            }
        }

        return result;
    }
}
