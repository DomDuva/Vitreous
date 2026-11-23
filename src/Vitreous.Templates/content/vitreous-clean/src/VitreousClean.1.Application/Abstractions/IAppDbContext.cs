using Microsoft.EntityFrameworkCore;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Application.Abstractions;

/// <summary>
/// The data the use cases work with. Handlers query and change it through EF Core directly: a
/// DbContext already is a unit of work, and each <see cref="DbSet{TEntity}"/> a repository.
/// Infrastructure implements it, so Application knows nothing about the database.
/// </summary>
public interface IAppDbContext
{
    DbSet<Order> Orders { get; }

    /// <summary>Saves the changes, then publishes the domain events of the saved entities.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
