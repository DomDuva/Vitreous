using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Vitreous.Abstractions;
using VitreousClean._1.Application.Abstractions;
using VitreousClean._1.Domain;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Infrastructure.Persistence;

/// <summary>
/// The EF Core context behind <see cref="IAppDbContext"/>. It maps the domain model without the
/// domain knowing about EF, and publishes the domain events of the entities it saves.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IPublisher publisher)
    : DbContext(options), IAppDbContext
{
    public DbSet<Order> Orders => Set<Order>();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        // Take the events before saving, publish them only once the save has succeeded, so a handler
        // never sees an order that wasn't stored. Each is typed INotification here; Publish
        // dispatches on its runtime type, so OrderPlaced reaches the OrderPlaced handlers.
        var domainEvents = ChangeTracker.Entries<IHasDomainEvents>()
            .SelectMany(static e => e.Entity.DequeueDomainEvents())
            .ToList();

        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);

        foreach (var domainEvent in domainEvents)
            await publisher.Publish(domainEvent, ct);

        return saved;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    // SQLite has no DateTimeOffset type and can't order by one, so it is stored as a sortable number.
    protected override void ConfigureConventions(ModelConfigurationBuilder configuration) =>
        configuration.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
}
