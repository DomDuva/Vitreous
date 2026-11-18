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
    /// <remarks>
    /// The multiplexer created from this string does not abort when Redis is unreachable
    /// (<c>abortConnect=false</c>) and fails commands fast while disconnected, so an outage
    /// degrades to cache misses instead of blocking or failing requests. It reconnects
    /// in the background.
    /// </remarks>
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

    /// <inheritdoc cref="VitreousCachingOptions.DefaultAbsoluteExpiration"/>
    public TimeSpan? DefaultAbsoluteExpiration { get; set; }

    /// <inheritdoc cref="VitreousCachingOptions.EnableStampedeProtection"/>
    public bool EnableStampedeProtection { get; set; } = true;

    /// <inheritdoc cref="VitreousCachingOptions.JsonSerializerOptions"/>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
