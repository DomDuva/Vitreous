using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vitreous.Abstractions;
using VitreousClean._1.Infrastructure.Persistence;

namespace VitreousClean._1.Application.Tests;

/// <summary>
/// The app's own DbContext on a SQLite in-memory database, so the handlers run real queries. The
/// database lives as long as its connection. The published domain events are recorded.
/// </summary>
public sealed class TestDatabase : IPublisher, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDatabase()
    {
        _connection.Open();

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public List<INotification> Published { get; } = [];

    /// <summary>A new context reads what was saved, not what another one still tracks.</summary>
    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, this);

    public void Dispose() => _connection.Dispose();

    ValueTask IPublisher.Publish<TNotification>(TNotification notification, CancellationToken ct)
    {
        Published.Add(notification);
        return ValueTask.CompletedTask;
    }
}
