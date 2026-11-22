using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Vitreous.Abstractions;
using Vitreous.Sample.DDD.Application.Abstractions;
using Vitreous.Sample.DDD.Domain;
using Vitreous.Sample.DDD.Domain.Orders;

namespace Vitreous.Sample.DDD.Infrastructure.Persistence;

/// <summary>
/// The EF Core context behind <see cref="IAppDbContext"/>. It maps the domain model without the
/// domain knowing about EF, and publishes the domain events of the entities it saves.
/// </summary>
internal sealed class AppDbContext(DbContextOptions<AppDbContext> options, IPublisher publisher)
    : DbContext(options), IAppDbContext
{
    public DbSet<Order> Orders => Set<Order>();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        // Take the events before saving, publish them only once the save has succeeded, so a
        // handler never sees an order that wasn't stored. Each is typed INotification here;
        // Publish dispatches on its runtime type, so OrderCreated reaches the OrderCreated handlers.
        var domainEvents = ChangeTracker.Entries<IHasDomainEvents>()
            .SelectMany(e => e.Entity.DequeueDomainEvents())
            .ToList();

        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);

        foreach (var domainEvent in domainEvents)
            await publisher.Publish(domainEvent, ct);

        return saved;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<Order>();

        // Order exposes get-only properties and a private constructor, so EF maps them explicitly
        // and binds the constructor's parameters by name.
        order.HasKey(o => o.Id);
        order.Property(o => o.CustomerName).IsRequired();
        order.Property(o => o.Product).IsRequired();
        order.Property(o => o.Quantity);

        // SQLite has no DateTimeOffset type and can't ORDER BY one, so store it as a sortable long.
        order.Property(o => o.CreatedAt).HasConversion(new DateTimeOffsetToBinaryConverter());
    }
}
