namespace Vitreous.Caching.Redis;

/// <summary>
/// Configuration options for the Vitreous Redis caching backend.
/// </summary>
public sealed class VitreousRedisCachingOptions
{
    /// <summary>
    /// Redis connection string (e.g. <c>"localhost:6379"</c> or a full StackExchange.Redis
    /// configuration string). Required when not providing an existing
    /// <see cref="IConnectionMultiplexer"/> in DI.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Prefix prepended to every cache key to avoid collisions.
    /// Default: <c>"vitreous:"</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "vitreous:";

    /// <summary>
    /// Prefix used for the Redis Sets that track tag → key mappings.
    /// Default: <c>"vtag:"</c>.
    /// </summary>
    public string TagPrefix { get; set; } = "vtag:";
}
