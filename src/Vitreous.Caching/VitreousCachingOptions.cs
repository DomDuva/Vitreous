namespace Vitreous.Caching;

/// <summary>
/// Configuration options for the Vitreous caching pipeline.
/// </summary>
public sealed class VitreousCachingOptions
{
    /// <summary>
    /// Prefix prepended to every cache key to avoid collisions with other applications
    /// sharing the same cache store. Default: <c>"vitreous:"</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "vitreous:";

    /// <summary>
    /// Absolute expiration applied when a query sets neither
    /// <see cref="Abstractions.ICacheableQuery{TResult}.AbsoluteExpiration"/> nor
    /// <see cref="Abstractions.ICacheableQuery{TResult}.SlidingExpiration"/>.
    /// Default: <see langword="null"/>, meaning such entries never expire.
    /// </summary>
    public TimeSpan? DefaultAbsoluteExpiration { get; set; }

    /// <summary>
    /// When <see langword="true"/> (the default), concurrent cache misses for the same key
    /// within this process share a single handler run instead of all calling the handler.
    /// Callers that share a run receive the same result instance.
    /// </summary>
    public bool EnableStampedeProtection { get; set; } = true;

    /// <summary>
    /// Options used by the default System.Text.Json cache serializer. Default: camelCase.
    /// Ignored when a custom <see cref="Abstractions.IVitreousCacheSerializer"/> is registered.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
