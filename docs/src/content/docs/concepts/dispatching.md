---
title: Dispatching
description: Sending commands and queries with ISender, publishing notifications with IPublisher, and consuming stream queries.
sidebar:
  order: 3
---

Inject `ISender` for commands and queries, `IPublisher` for notifications.

```csharp
app.MapPost("/orders", async (CreateOrderCommand command, ISender sender) =>
{
    var result = await sender.Send(command);
    return result.Match(
        id   => Results.Created($"/orders/{id}", new { id }),
        err  => Results.Problem(err.Message)
    );
});

app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender) =>
{
    var result = await sender.Send(new GetOrderQuery(id));
    return result.Match(
        order => Results.Ok(order),
        err   => Results.Problem(err.Message)
    );
});

app.MapPost("/orders/{id:guid}/shipped", async (Guid id, IPublisher publisher) =>
{
    await publisher.Publish(new OrderCreatedNotification(id));
    return Results.NoContent();
});
```

`ISender` and `IPublisher` are scoped unless `DefaultHandlerLifetime` is `Singleton`. Minimal APIs and controllers resolve them from the request scope. In a `BackgroundService` or another singleton, create a scope with `IServiceScopeFactory` first. See [Installation](/Vitreous/getting-started/installation/#options).

## Which handlers a notification reaches

`Publish` dispatches on the notification's runtime type, as `Send` does for messages. A notification held in a variable typed `INotification` or as a base type, such as an entry in an aggregate's list of domain events, still reaches the handlers of its concrete type:

```csharp
IReadOnlyList<INotification> domainEvents = order.DequeueDomainEvents();

foreach (var domainEvent in domainEvents)
    await publisher.Publish(domainEvent, ct); // runs the OrderCreated handlers for an OrderCreated
```

- The handlers of the notification's concrete type run.
- If it has none, the handlers of its nearest base class that has handlers run.
- If neither has handlers, the handlers for the type argument run, such as the `INotificationHandler<IOrderEvent>` handlers for `Publish<IOrderEvent>(...)`.

:::note
Before 0.5.1, `Publish` looked up handlers by the type argument only, so a notification held as `INotification` reached no handlers.
:::

## Stream queries

Inject `ISender` and call `CreateStream<TResult>()`. The returned `Result` wraps the lazy `IAsyncEnumerable<TResult>` — check it for setup failures before consuming the stream.

```csharp
app.MapGet("/orders/stream/tick", async (int count, ISender sender, CancellationToken ct) =>
{
    var result = await sender.CreateStream(new GetOrderTicksQuery(count), ct);
    return result.IsSuccess
        ? Results.Ok(result.Value)      // IAsyncEnumerable<OrderDto> streamed as JSON array
        : Results.Problem(result.Error.Message);
});
```

## Stream query helpers

`StreamQueryExtensions` provides ergonomic helpers for working with the `Result<IAsyncEnumerable<T>>` returned by `CreateStream`.

### CollectStream: eager collection

Opens the stream and awaits all items into a `List<T>`. Useful for tests and one-shot scenarios where the full result is needed at once. Captures both setup failures and mid-stream exceptions.

```csharp
Result<List<OrderDto>> result = await sender.CollectStream(new GetOrderTicksQuery(3), ct);
```

### MapStream: lazy projection

Projects each item with a synchronous mapper. Equivalent to LINQ `Select` — no buffering.

```csharp
Result<IAsyncEnumerable<string>> names = (await sender.CreateStream(query, ct)).MapStream(o => o.CustomerName);
```

### FilterStream: lazy filtering

Keeps only items satisfying a predicate. Equivalent to LINQ `Where` — no buffering.

```csharp
Result<IAsyncEnumerable<OrderDto>> large = (await sender.CreateStream(query, ct)).FilterStream(o => o.Quantity > 10);
```

### TakeStream: lazy truncation

Stops the stream after at most `count` items. Equivalent to LINQ `Take` — no buffering.

```csharp
Result<IAsyncEnumerable<OrderDto>> first3 = (await sender.CreateStream(query, ct)).TakeStream(3);
```

All three lazy helpers pass failures through untouched — if `CreateStream` returned a failure `Result`, the helper returns the same failure without opening the stream.
