using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vitreous.Sample.DDD.Application.Abstractions;
using Vitreous.Sample.DDD.Infrastructure.Persistence;

namespace Vitreous.Sample.DDD.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="IAppDbContext"/>, backed by EF Core on a SQLite in-memory database, so
    /// the sample needs no database server or file. Swap <c>UseSqlite</c> for your provider in a
    /// real app. The notification handlers in this project need no registration: the source
    /// generator in the host finds them.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // A shared-cache in-memory database lives while at least one connection to it is open.
        // The container owns that connection (and closes it, dropping the database, on shutdown);
        // each DbContext opens its own. The name is unique per app, so test hosts don't share data.
        var connectionString = $"Data Source=orders-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        });
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        return services;
    }

    /// <summary>Opens the database and creates its schema. Call once, after building the app.</summary>
    public static void EnsureDatabaseCreated(this IServiceProvider services)
    {
        services.GetRequiredService<SqliteConnection>();

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }
}
