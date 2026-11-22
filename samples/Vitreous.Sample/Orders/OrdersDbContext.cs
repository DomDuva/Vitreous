using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Vitreous.Sample.Orders;

/// <summary>
/// The sample's EF Core context. Handlers use it directly: a <see cref="DbContext"/> already is a
/// unit of work and each <see cref="DbSet{TEntity}"/> a repository, so there is no layer on top.
/// </summary>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        // SQLite has no DateTimeOffset type and can't ORDER BY one, so store it as a sortable long.
        modelBuilder.Entity<Order>()
            .Property(o => o.CreatedAt)
            .HasConversion(new DateTimeOffsetToBinaryConverter());
}
