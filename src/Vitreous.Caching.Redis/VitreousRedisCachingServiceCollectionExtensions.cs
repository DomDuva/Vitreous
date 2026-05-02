using Vitreous.Caching;
using Vitreous.Caching.Redis.Internal;

namespace Vitreous.Caching.Redis;

/// <summary>
/// Extension methods to register the Vitreous Redis caching backend.
/// </summary>
public static class VitreousRedisCachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers Vitreous caching behaviors backed by Redis, with full support for
    /// tag-based cache invalidation via <see cref="IInvalidatesCache.InvalidatesTags"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configure Redis connection and key/tag prefixes.</param>
    /// <remarks>
    /// This registers <c>RedisVitreousCache</c> as the <see cref="IVitreousCache"/>
    /// implementation and sets up an <see cref="IConnectionMultiplexer"/> from the
    /// provided connection string. If an <see cref="IConnectionMultiplexer"/> is already
    /// registered in DI (e.g. by your own configuration), omit the connection string and
    /// the existing multiplexer will be used.
    /// </remarks>
    public static IServiceCollection AddVitreousRedisCaching(
        this IServiceCollection services,
        Action<VitreousRedisCachingOptions>? configure = null)
    {
        var options = new VitreousRedisCachingOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // Register the Redis multiplexer only if not already in DI
        if (options.ConnectionString is not null)
        {
            services.TryAddSingleton<IConnectionMultiplexer>(
                _ => ConnectionMultiplexer.Connect(options.ConnectionString));
        }

        // Register the VitreousCachingOptions with defaults (behaviours need it)
        services.TryAddSingleton(new VitreousCachingOptions
        {
            KeyPrefix = options.KeyPrefix
        });

        // Replace the default IDistributedCache-backed store with the Redis store
        services.AddSingleton<IVitreousCache, RedisVitreousCache>();

        // Register the shared pipeline behaviors from Vitreous.Caching
        services.AddVitreousCachingBehaviors();

        return services;
    }

    /// <summary>
    /// Registers only the caching pipeline behaviors (without touching <see cref="IVitreousCache"/>).
    /// Used internally by <see cref="AddVitreousRedisCaching"/>.
    /// </summary>
    internal static IServiceCollection AddVitreousCachingBehaviors(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton(
            typeof(Vitreous.Abstractions.IPipelineBehavior<,>),
            typeof(Vitreous.Caching.Behaviors.CacheInvalidationBehavior<,>)));

        services.TryAddEnumerable(ServiceDescriptor.Singleton(
            typeof(Vitreous.Abstractions.IPipelineBehavior<,>),
            typeof(Vitreous.Caching.Behaviors.CachingBehavior<,>)));

        return services;
    }
}
