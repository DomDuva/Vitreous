using System.Collections.Concurrent;

namespace VitreousWorker._1.Features.Reminders;

public sealed record Reminder(Guid Id, string Recipient, string Text, DateTimeOffset DueAt);

/// <summary>
/// Keeps the pending reminders in memory, so the project runs with no setup. When you add a
/// database, the handlers use your EF Core DbContext directly in place of this class.
/// </summary>
public sealed class ReminderStore
{
    private readonly ConcurrentDictionary<Guid, Reminder> _pending = new();

    public int PendingCount => _pending.Count;

    public Reminder Add(string recipient, string text, DateTimeOffset dueAt)
    {
        var reminder = new Reminder(Guid.NewGuid(), recipient, text, dueAt);
        _pending[reminder.Id] = reminder;
        return reminder;
    }

    public List<Reminder> DueAt(DateTimeOffset now) =>
        [.. _pending.Values.Where(r => r.DueAt <= now).OrderBy(static r => r.DueAt)];

    public bool IsPending(Guid id) => _pending.ContainsKey(id);

    public void MarkSent(Guid id) => _pending.TryRemove(id, out _);
}
