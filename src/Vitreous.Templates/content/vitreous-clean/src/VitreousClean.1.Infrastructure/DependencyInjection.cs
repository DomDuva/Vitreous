using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
#if (caching == "memory")
using Vitreous.Caching;
#elif (caching == "redis")
using Vitreous.Caching.Redis;
#endif
using VitreousClean._1.Application.Abstractions;
using VitreousClean._1.Infrastructure.Email;
using VitreousClean._1.Infrastructure.Persistence;

namespace VitreousClean._1.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the implementation of everything Application declares in its Abstractions.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(configuration.GetConnectionString("Database")));
        services.AddScoped<IAppDbContext>(static sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IEmailSender, LogEmailSender>();
#if (caching == "memory")

        // Caches the result of every ICacheableQuery. The in-memory IDistributedCache suits one instance;
        // switch to Vitreous.Caching.Redis when you run several.
        services.AddDistributedMemoryCache();
        services.AddVitreousCaching();
#elif (caching == "redis")

        // Caches the result of every ICacheableQuery in Redis (compose.yaml runs one locally). If Redis is
        // down, queries still run: cache errors count as misses.
        services.AddVitreousRedisCaching(o => o.ConnectionString = configuration.GetConnectionString("Redis"));
#endif

        return services;
    }

    /// <summary>
    /// Creates the database if it doesn't exist. It doesn't update one that does: switch to EF Core
    /// migrations before the schema has to change without losing data.
    /// </summary>
    public static async Task EnsureDatabaseCreatedAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    }
}
