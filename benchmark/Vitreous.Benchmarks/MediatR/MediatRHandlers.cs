using MediatR;

namespace Vitreous.Benchmarks.MediatRHandlers;

// ── Query (returns a value) ───────────────────────────────────────────────────

public sealed record BenchQuery : IRequest<int>;

public sealed class BenchQueryHandler : IRequestHandler<BenchQuery, int>
{
    public Task<int> Handle(BenchQuery request, CancellationToken ct)
        => Task.FromResult(42);
}

// ── Command (returns void / Unit) ─────────────────────────────────────────────

public sealed record BenchVoidCommand : IRequest;

public sealed class BenchVoidCommandHandler : IRequestHandler<BenchVoidCommand>
{
    public Task Handle(BenchVoidCommand request, CancellationToken ct)
        => Task.CompletedTask;
}

// ── Notification ──────────────────────────────────────────────────────────────

public sealed record BenchNotification : INotification;

public sealed class BenchNotificationHandler : INotificationHandler<BenchNotification>
{
    public Task Handle(BenchNotification notification, CancellationToken ct)
        => Task.CompletedTask;
}

// ── Pass-through pipeline behavior ───────────────────────────────────────────
// Does nothing except call next() — used to measure the per-behavior overhead.

public sealed class BenchPassThroughBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
        => next();
}
