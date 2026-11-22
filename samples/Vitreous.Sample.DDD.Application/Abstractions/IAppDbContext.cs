using Microsoft.EntityFrameworkCore;
using Vitreous.Sample.DDD.Domain.Orders;

namespace Vitreous.Sample.DDD.Application.Abstractions;

/// <summary>
/// The data the use cases work with. Handlers query and change it through EF Core directly, with no
/// repository on top: a DbContext already is a unit of work, and each <see cref="DbSet{TEntity}"/>
/// a repository. Infrastructure's <c>AppDbContext</c> implements it, so Application stays
/// independent of the database provider and of Infrastructure.
/// </summary>
public interface IAppDbContext
{
    DbSet<Order> Orders { get; }

    /// <summary>Saves the changes, then publishes the domain events of the saved entities.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
