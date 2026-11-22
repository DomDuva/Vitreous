using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Vitreous.Sample.Caching.Orders;

/// <summary>
/// Sets up <see cref="OrdersDbContext"/> on a SQLite in-memory database, so the sample needs no
/// database server or file. Swap <c>UseSqlite</c> for your provider in a real app.
/// </summary>
public static class OrdersDatabase
{
    public static IServiceCollection AddOrdersDatabase(this IServiceCollection services)
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
        services.AddDbContext<OrdersDbContext>(o => o.UseSqlite(connectionString));

        return services;
    }

    /// <summary>Opens the database and creates its schema. Call once, after building the app.</summary>
    public static void EnsureOrdersDatabaseCreated(this IServiceProvider services)
    {
        services.GetRequiredService<SqliteConnection>();

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.EnsureCreated();
    }
}
