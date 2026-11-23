using Vitreous;
using Vitreous.Abstractions;

namespace VitreousWorker._1.Features.Reminders;

public sealed record GetDueRemindersQuery : IQuery<List<Reminder>>;

public sealed class GetDueRemindersHandler(ReminderStore store, TimeProvider time) : IQueryHandler<GetDueRemindersQuery, List<Reminder>>
{
    public ValueTask<Result<List<Reminder>>> Handle(GetDueRemindersQuery query, CancellationToken ct = default) =>
        new(store.DueAt(time.GetUtcNow()));
}
