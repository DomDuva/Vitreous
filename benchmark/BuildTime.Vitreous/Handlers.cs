using Vitreous;
using Vitreous.Abstractions;

namespace BuildTime.VitreousApp;

// ── Query ─────────────────────────────────────────────────────────────────────

public sealed record GetItemQuery(int Id) : IQuery<string>;

public sealed class GetItemQueryHandler : IQueryHandler<GetItemQuery, string>
{
    public ValueTask<Result<string>> Handle(GetItemQuery query, CancellationToken ct = default)
        => new(Result<string>.Ok($"Item #{query.Id}"));
}

// ── Command (returns void) ────────────────────────────────────────────────────

public sealed record DeleteItemCommand(int Id) : ICommand;

public sealed class DeleteItemCommandHandler : ICommandHandler<DeleteItemCommand>
{
    public ValueTask<Result> Handle(DeleteItemCommand command, CancellationToken ct = default)
        => new(Result.Success);
}

// ── Notification ──────────────────────────────────────────────────────────────

public sealed record ItemDeletedNotification(int Id) : INotification;

public sealed class ItemDeletedHandler : INotificationHandler<ItemDeletedNotification>
{
    public ValueTask Handle(ItemDeletedNotification notification, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
