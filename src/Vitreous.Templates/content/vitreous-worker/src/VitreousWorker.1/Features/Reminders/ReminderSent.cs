using Vitreous.Abstractions;

namespace VitreousWorker._1.Features.Reminders;

// A notification goes to every handler registered for it: zero, one or many. Add another
// INotificationHandler<ReminderSent> anywhere in the project and the generator wires it up.
public sealed record ReminderSent(Guid Id, DateTimeOffset SentAt) : INotification;

public sealed class LogReminderSent(ReminderStore store, ILogger<LogReminderSent> logger) : INotificationHandler<ReminderSent>
{
    public ValueTask Handle(ReminderSent notification, CancellationToken ct = default)
    {
        logger.LogInformation("Reminder {ReminderId} sent, {Pending} pending", notification.Id, store.PendingCount);
        return ValueTask.CompletedTask;
    }
}
