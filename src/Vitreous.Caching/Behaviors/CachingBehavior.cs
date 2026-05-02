namespace Vitreous.Caching.Behaviors;

/// <summary>
/// Pipeline behavior that short-circuits query execution on a cache hit.
/// Only active for messages that implement <see cref="ICacheableQuery{TResult}"/>;
/// all other messages pass through unchanged.
/// </summary>
/// <remarks>
/// Register this as the innermost behavior (closest to the handler) so outer behaviors
/// such as logging and tracing still execute on cache hits.
/// On a hit the handler is never called. On a miss the result is cached only when
/// <see cref="Result{T}.IsSuccess"/> is <see langword="true"/>.
/// Cache failures (e.g. Redis unavailable) are logged as warnings and the handler
/// is called as a fallback — the cache is always a performance layer, never a hard dependency.
/// </remarks>
public sealed class CachingBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
{
    private readonly IVitreousCache _cache;
    private readonly ILogger<CachingBehavior<TMessage, TResult>> _logger;
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CachingBehavior(IVitreousCache cache, ILogger<CachingBehavior<TMessage, TResult>> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    public async ValueTask<Result<TResult>> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        if (message is not ICacheableQuery<TResult> cacheable)
            return await next(message, ct);

        // ── Cache read ────────────────────────────────────────────────────────
        try
        {
            var (hit, cached) = await _cache.GetAsync<TResult>(cacheable.CacheKey, ct);
            if (hit)
            {
                _logger.LogDebug(
                    "Vitreous.Caching: hit for {MessageType} key='{Key}'",
                    typeof(TMessage).Name, cacheable.CacheKey);

                // Signal to outer pipeline behaviors (e.g. TracingBehavior) that this
                // result was served from cache, not from the handler.
                if (VitreousContext.Current is { } annotations)
                    annotations.IsCacheHit = true;

                return Result<TResult>.Ok(cached!);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Vitreous.Caching: read failed for {MessageType} key='{Key}', falling back to handler",
                typeof(TMessage).Name, cacheable.CacheKey);
        }

        // ── Handler ───────────────────────────────────────────────────────────
        var result = await next(message, ct);

        // ── Cache write (success only) ─────────────────────────────────────────
        if (result.IsSuccess && result.Value is not null)
        {
            try
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = cacheable.AbsoluteExpiration,
                    SlidingExpiration               = cacheable.SlidingExpiration
                };
                await _cache.SetAsync(cacheable.CacheKey, result.Value, cacheable.Tags, options, ct);

                _logger.LogDebug(
                    "Vitreous.Caching: stored {MessageType} key='{Key}' (abs={Abs} sliding={Slide})",
                    typeof(TMessage).Name, cacheable.CacheKey,
                    cacheable.AbsoluteExpiration, cacheable.SlidingExpiration);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Vitreous.Caching: write failed for {MessageType} key='{Key}'",
                    typeof(TMessage).Name, cacheable.CacheKey);
            }
        }

        return result;
    }
}
