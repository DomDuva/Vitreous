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
}
