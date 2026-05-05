using MediatR;

namespace BuildTime.MediatRApp;

// ── Query ─────────────────────────────────────────────────────────────────────

public sealed record GetItemQuery(int Id) : IRequest<string>;

public sealed class GetItemQueryHandler : IRequestHandler<GetItemQuery, string>
{
    public Task<string> Handle(GetItemQuery request, CancellationToken ct)
        => Task.FromResult($"Item #{request.Id}");
}

// ── Command (returns void) ────────────────────────────────────────────────────

public sealed record DeleteItemCommand(int Id) : IRequest;

public sealed class DeleteItemCommandHandler : IRequestHandler<DeleteItemCommand>
{
    public Task Handle(DeleteItemCommand request, CancellationToken ct)
        => Task.CompletedTask;
}

// ── Notification ──────────────────────────────────────────────────────────────

public sealed record ItemDeletedNotification(int Id) : INotification;

public sealed class ItemDeletedHandler : INotificationHandler<ItemDeletedNotification>
{
    public Task Handle(ItemDeletedNotification notification, CancellationToken ct)
        => Task.CompletedTask;
}
