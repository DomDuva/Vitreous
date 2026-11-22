using Vitreous.Caching;
using Vitreous.Caching.Redis.Internal;
using Vitreous.Caching.Serialization;

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
    /// implementation. When <see cref="VitreousRedisCachingOptions.ConnectionString"/> is set,
    /// an <see cref="IConnectionMultiplexer"/> is registered from it (unless one is already
    /// registered). Otherwise register your own <see cref="IConnectionMultiplexer"/>, before or
    /// after calling this method.
    /// <para>
    /// The multiplexer is only resolved the first time a cacheable query or invalidating command
    /// runs, so other messages never wait on, or fail because of, Redis.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVitreousRedisCaching(
        this IServiceCollection services,
        Action<VitreousRedisCachingOptions>? configure = null)
    {
        var options = new VitreousRedisCachingOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            var connectionString = options.ConnectionString!;
            services.TryAddSingleton<IConnectionMultiplexer>(_ => Connect(connectionString));
        }

        // Behaviours read these (default expiry, stampede protection, serializer options)
        services.TryAddSingleton(new VitreousCachingOptions
        {
            KeyPrefix                 = options.KeyPrefix,
            DefaultAbsoluteExpiration = options.DefaultAbsoluteExpiration,
            EnableStampedeProtection  = options.EnableStampedeProtection,
            JsonSerializerOptions     = options.JsonSerializerOptions,
        });

        services.TryAddSingleton<IVitreousCacheSerializer>(static sp =>
            new SystemTextJsonVitreousCacheSerializer(sp.GetRequiredService<VitreousCachingOptions>().JsonSerializerOptions));

        // Replace the default IDistributedCache-backed store with the Redis store
        services.AddSingleton<IVitreousCache>(static sp => new RedisVitreousCache(
            () => sp.GetService<IConnectionMultiplexer>() ?? throw new InvalidOperationException(
                "Vitreous.Caching.Redis needs a Redis connection. Either set " +
                "options.ConnectionString in AddVitreousRedisCaching() (e.g. \"localhost:6379\") " +
                "or register an IConnectionMultiplexer in the service collection."),
            sp.GetRequiredService<VitreousRedisCachingOptions>(),
            sp.GetRequiredService<IVitreousCacheSerializer>(),
            sp.GetRequiredService<ILogger<RedisVitreousCache>>()));

        // Register the shared pipeline behaviors from Vitreous.Caching
        services.AddVitreousCachingBehaviors();

        return services;
    }

    private static ConnectionMultiplexer Connect(string connectionString)
    {
        var configuration = ConfigurationOptions.Parse(connectionString);

        // A cache must never take the application down: don't throw when Redis is unreachable
        // at startup, and fail commands immediately while disconnected instead of queueing them
        // until they time out. The behaviors treat those failures as cache misses.
        configuration.AbortOnConnectFail = false;
        configuration.BacklogPolicy      = BacklogPolicy.FailFast;

        return ConnectionMultiplexer.Connect(configuration);
    }

    /// <summary>
    /// Registers only the caching pipeline behaviors (without touching <see cref="IVitreousCache"/>).
    /// Used internally by <see cref="AddVitreousRedisCaching"/>.
    /// </summary>
    internal static IServiceCollection AddVitreousCachingBehaviors(this IServiceCollection services)
    {
        // AddVitreousBehavior closes them over every message when the source generator is used,
        // so they work under Native AOT for value-type messages and results.
        services.AddVitreousBehavior(typeof(Vitreous.Caching.Behaviors.CacheInvalidationBehavior<,>), ServiceLifetime.Singleton);
        services.AddVitreousBehavior(typeof(Vitreous.Caching.Behaviors.CachingBehavior<,>), ServiceLifetime.Singleton);

        return services;
    }
}
