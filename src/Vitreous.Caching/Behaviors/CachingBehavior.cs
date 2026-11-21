using System.Collections.Concurrent;

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
/// <para>
/// With <see cref="VitreousCachingOptions.EnableStampedeProtection"/> on (the default),
/// concurrent misses for the same key share one handler run. If the caller running the
/// handler is canceled, the others run it themselves.
/// </para>
/// </remarks>
public sealed class CachingBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
{
    private static readonly string? s_messageType = typeof(TMessage).FullName;

    private readonly IVitreousCache _cache;
    private readonly ILogger<CachingBehavior<TMessage, TResult>> _logger;
    private readonly TimeSpan? _defaultExpiration;
    private readonly bool _stampedeProtection;

    // Misses currently being loaded, by cache key. Per behavior instance (one per closed
    // generic type per container), so separate service providers never share loads.
    private readonly ConcurrentDictionary<string, Task<Result<TResult>>> _inflight = new(StringComparer.Ordinal);

    public CachingBehavior(IVitreousCache cache, ILogger<CachingBehavior<TMessage, TResult>> logger)
        : this(cache, logger, options: null)
    {
    }

    public CachingBehavior(
        IVitreousCache cache,
        ILogger<CachingBehavior<TMessage, TResult>> logger,
        VitreousCachingOptions? options)
    {
        _cache              = cache;
        _logger             = logger;
        _defaultExpiration  = options?.DefaultAbsoluteExpiration;
        _stampedeProtection = options?.EnableStampedeProtection ?? true;
    }

    public async ValueTask<Result<TResult>> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        if (message is not ICacheableQuery<TResult> cacheable)
            return await next(message, ct);

        var key = cacheable.CacheKey;

        // ── Cache read ────────────────────────────────────────────────────────
        try
        {
            var (hit, cached) = await _cache.GetAsync<TResult>(key, ct);

            // A stored null cannot be returned as a successful Result; treat it as a miss.
            if (hit && cached is not null)
            {
                _logger.LogDebug(
                    "Vitreous.Caching: hit for {MessageType} key='{Key}'",
                    typeof(TMessage).Name, key);

                // Signal to outer pipeline behaviors (e.g. TracingBehavior) that this
                // result was served from cache, not from the handler.
                if (VitreousContext.Current is { } annotations)
                    annotations.IsCacheHit = true;

                VitreousCachingTelemetry.RecordRequest("hit", s_messageType);
                return Result<TResult>.Ok(cached);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Vitreous.Caching: read failed for {MessageType} key='{Key}', falling back to handler",
                typeof(TMessage).Name, key);
        }

        return _stampedeProtection
            ? await LoadOnceAsync(message, next, cacheable, key, ct)
            : await LoadAndStoreAsync(message, next, cacheable, ct);
    }

    /// <summary>
    /// Runs the handler for <paramref name="key"/> unless another caller already is, in which
    /// case its result is shared.
    /// </summary>
    private async ValueTask<Result<TResult>> LoadOnceAsync(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        ICacheableQuery<TResult> cacheable,
        string key,
        CancellationToken ct)
    {
        var load = new TaskCompletionSource<Result<TResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inflight = _inflight.GetOrAdd(key, load.Task);

        if (!ReferenceEquals(inflight, load.Task))
        {
            try
            {
                var shared = await inflight.WaitAsync(ct);
                VitreousCachingTelemetry.RecordRequest("coalesced", s_messageType);
                return shared;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The caller that was running the handler was canceled; we were not.
                return await LoadAndStoreAsync(message, next, cacheable, ct);
            }
        }

        try
        {
            var result = await LoadAndStoreAsync(message, next, cacheable, ct);
            load.TrySetResult(result);
            return result;
        }
        catch (OperationCanceledException oce)
        {
            load.TrySetCanceled(oce.CancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            load.TrySetException(ex);
            _ = load.Task.Exception; // observed here; waiters (if any) get it rethrown
            throw;
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<string, Task<Result<TResult>>>(key, load.Task));
        }
    }

    private async ValueTask<Result<TResult>> LoadAndStoreAsync(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        ICacheableQuery<TResult> cacheable,
        CancellationToken ct)
    {
        // ── Handler ───────────────────────────────────────────────────────────
        VitreousCachingTelemetry.RecordRequest("miss", s_messageType);
        var result = await next(message, ct);

        // ── Cache write (success only) ─────────────────────────────────────────
        if (result.IsSuccess && result.Value is not null)
        {
            try
            {
                var absolute = cacheable.AbsoluteExpiration
                    ?? (cacheable.SlidingExpiration is null ? _defaultExpiration : null);

                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = absolute,
                    SlidingExpiration               = cacheable.SlidingExpiration
                };
                await _cache.SetAsync(cacheable.CacheKey, result.Value, cacheable.Tags, options, ct);

                _logger.LogDebug(
                    "Vitreous.Caching: stored {MessageType} key='{Key}' (abs={Abs} sliding={Slide})",
                    typeof(TMessage).Name, cacheable.CacheKey,
                    absolute, cacheable.SlidingExpiration);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "Vitreous.Caching: write failed for {MessageType} key='{Key}'",
                    typeof(TMessage).Name, cacheable.CacheKey);
            }
        }

        return result;
    }
}
