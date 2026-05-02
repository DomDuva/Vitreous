using Vitreous.Caching.Behaviors;
using Vitreous.Caching.Internal;

namespace Vitreous.Caching;

/// <summary>
/// Extension methods to register Vitreous caching pipeline behaviors.
/// </summary>
public static class VitreousCachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Vitreous caching pipeline behaviors backed by <see cref="IDistributedCache"/>.
    /// </summary>
    /// <remarks>
    /// You must independently register an <see cref="IDistributedCache"/> implementation before
    /// calling this, for example:
    /// <code>
    /// // In-process memory cache (single instance):
    /// services.AddDistributedMemoryCache();
    ///
    /// // Redis (distributed, multi-instance):
    /// services.AddStackExchangeRedisCache(o => o.Configuration = "localhost");
    /// </code>
    /// For full tag-based invalidation with Redis, use
    /// <c>AddVitreousRedisCaching()</c> from the <c>Vitreous.Caching.Redis</c> package instead.
    /// </remarks>
    public static IServiceCollection AddVitreousCaching(
        this IServiceCollection services,
        Action<VitreousCachingOptions>? configure = null)
    {
        var options = new VitreousCachingOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IVitreousCache, DistributedVitreousCache>();

        // CachingBehavior must be innermost (registered last) so it short-circuits
        // the handler only — outer behaviors (tracing, logging) still run on cache hits.
        services.TryAddEnumerable(ServiceDescriptor.Singleton(
            typeof(IPipelineBehavior<,>),
            typeof(CacheInvalidationBehavior<,>)));

        services.TryAddEnumerable(ServiceDescriptor.Singleton(
            typeof(IPipelineBehavior<,>),
            typeof(CachingBehavior<,>)));

        return services;
    }
}
