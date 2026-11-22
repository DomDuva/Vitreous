---
title: Handlers
description: Implementing command, query, notification and stream query handlers.
sidebar:
  order: 2
---

Implement the corresponding handler interface for each message.

```csharp
// ICommand<TResult>
public sealed class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Guid>
{
    public async ValueTask<Result<Guid>> Handle(CreateOrderCommand command, CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        // ... persist order
        return id; // implicit conversion T → Result<T>
    }
}

// ICommand (void)
public sealed class DeleteOrderHandler : ICommandHandler<DeleteOrderCommand>
{
    public async ValueTask<Result> Handle(DeleteOrderCommand command, CancellationToken ct = default)
    {
        // ... delete order
        return Result.Success;
    }
}

// IQuery<TResult>
public sealed class GetOrderHandler : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async ValueTask<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct = default)
    {
        var order = await _repo.FindAsync(query.OrderId, ct);
        if (order is null) return Error.NotFound($"Order {query.OrderId} not found.");
        return order.ToDto(); // implicit conversion T → Result<T>
    }
}

// INotification — multiple handlers are allowed
public sealed class SendOrderConfirmationEmail : INotificationHandler<OrderCreatedNotification>
{
    public async ValueTask Handle(OrderCreatedNotification notification, CancellationToken ct = default)
    {
        // ... send email
    }
}
```

The source generator emits a diagnostic if a command or query has no handler (`VIT001`, warning) or more than one handler (`VIT002`, error). See [Diagnostics](/Vitreous/reference/diagnostics/).

Handlers must be non-abstract, non-generic classes or records that are `public` or `internal`. Private nested handlers cannot be registered and are reported as `VIT001`.

## Stream query handlers

Stream queries have two handler patterns.

### StreamQueryHandler base class

**`StreamQueryHandler<TQuery, TResult>`** is recommended for most cases. Override the single `Stream` method and use `yield return` freely. The base class wraps it in a successful `Result` automatically.

```csharp
public sealed class GetOrderTicksHandler : StreamQueryHandler<GetOrderTicksQuery, OrderDto>
{
    protected override async IAsyncEnumerable<OrderDto> Stream(
        GetOrderTicksQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var count = Math.Clamp(query.Count, 1, 20);
        for (var i = 1; i <= count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            yield return new OrderDto(Guid.NewGuid(), $"Customer {i}", "Widget", i, DateTimeOffset.UtcNow);
        }
    }
}
```

Always decorate the `CancellationToken` parameter with `[EnumeratorCancellation]` so that callers using `.WithCancellation(ct)` can cancel mid-stream (the source generator emits `VIT003` if this is missing).

### IStreamQueryHandler interface

Implement **`IStreamQueryHandler<TQuery, TResult>`** when validation is required. C# does not allow `yield return` in a method that returns `ValueTask<…>`, so split the handler into two methods: `Handle` for setup/validation and a private iterator for item production. Return a failure `Result` from `Handle` to signal errors before any item is produced.

```csharp
public sealed class GetOrderTicksHandler : IStreamQueryHandler<GetOrderTicksQuery, OrderDto>
{
    public ValueTask<Result<IAsyncEnumerable<OrderDto>>> Handle(
        GetOrderTicksQuery query, CancellationToken ct = default)
    {
        if (query.Count <= 0)
            return new(Error.Validation("Count must be positive."));

        return new(Result<IAsyncEnumerable<OrderDto>>.Ok(Stream(query, ct)));
    }

    private async IAsyncEnumerable<OrderDto> Stream(
        GetOrderTicksQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 1; i <= query.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            yield return new OrderDto(Guid.NewGuid(), $"Customer {i}", "Widget", i, DateTimeOffset.UtcNow);
        }
    }
}
```
