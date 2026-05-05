using Vitreous;
using Vitreous.Abstractions;

namespace Vitreous.Benchmarks.VitreousHandlers;

// ── Query (returns a value) ───────────────────────────────────────────────────

public sealed record BenchQuery : IQuery<int>;

public sealed class BenchQueryHandler : IQueryHandler<BenchQuery, int>
{
    public ValueTask<Result<int>> Handle(BenchQuery query, CancellationToken ct = default)
        => new(Result<int>.Ok(42));
}

// ── Command (returns void / Result) ──────────────────────────────────────────

public sealed record BenchVoidCommand : ICommand;

public sealed class BenchVoidCommandHandler : ICommandHandler<BenchVoidCommand>
{
    public ValueTask<Result> Handle(BenchVoidCommand command, CancellationToken ct = default)
        => new(Result.Success);
}

// ── Notification ──────────────────────────────────────────────────────────────

public sealed record BenchNotification : INotification;

public sealed class BenchNotificationHandler : INotificationHandler<BenchNotification>
{
    public ValueTask Handle(BenchNotification notification, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}

// ── Pass-through pipeline behavior ───────────────────────────────────────────
// Does nothing except call next() — used to measure the per-behavior overhead.

public sealed class BenchPassThroughBehavior<TMessage, TResult>
    : IPipelineBehavior<TMessage, TResult>
{
    public ValueTask<Result<TResult>> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
        => next(message, ct);
}
