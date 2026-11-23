using Microsoft.Extensions.Options;
using Vitreous.Abstractions;

namespace VitreousWorker._1.Features.Reminders;

public sealed class ReminderOptions
{
    /// <summary>How often the worker looks for due reminders.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Looks for due reminders on a timer and sends a command for each. It only decides when work
/// happens: what happens is in the handlers, which can be tested without a timer or a host.
/// </summary>
/// <remarks>
/// A hosted service is a singleton, and <see cref="ISender"/> is scoped. So the worker can't take
/// an ISender in its constructor: it takes a scope factory and opens a scope per unit of work.
/// </remarks>
public sealed class ReminderWorker(
    IServiceScopeFactory scopes,
    IOptions<ReminderOptions> options,
    TimeProvider time,
    ILogger<ReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // PeriodicTimer never overlaps: when an iteration takes longer than the interval, the next
        // one starts right after it.
        using var timer = new PeriodicTimer(options.Value.Interval, time);

        try
        {
            do
            {
                await RunOnce(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Stopping, no more reminders will be sent");
        }
    }

    /// <summary>One iteration: sends every reminder that is due now.</summary>
    public async Task RunOnce(CancellationToken ct)
    {
        IReadOnlyList<Reminder> due = [];

        await InScope(async sender =>
        {
            var result = await sender.Send(new GetDueRemindersQuery(), ct);
            if (result.IsSuccess)
                due = result.Value;
        }, ct);

        foreach (var reminder in due)
        {
            // LoggingBehavior logs a failed Result. The reminder then stays due, and the next
            // iteration tries it again.
            await InScope(
                async sender => await sender.Send(new SendReminderCommand(reminder.Id, reminder.Recipient, reminder.Text), ct),
                ct);
        }
    }

    // A scope per unit of work: each one gets its own handlers (and DbContext), and one that fails
    // leaves nothing behind for the next.
    private async Task InScope(Func<ISender, Task> work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await work(scope.ServiceProvider.GetRequiredService<ISender>());
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // An exception that leaves ExecuteAsync stops the whole host. Log it and carry on.
            logger.LogError(ex, "A unit of work failed unexpectedly");
        }
    }
}
