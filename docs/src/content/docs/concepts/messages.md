---
title: Messages
description: Commands, queries, stream queries and notifications.
sidebar:
  order: 1
---

Vitreous has three kinds of messages, plus stream queries for lazy sequences.

## Commands

Commands mutate state, return a result, and require exactly one handler.

```csharp
// Command with a return value
public sealed record CreateOrderCommand(Guid CustomerId, IReadOnlyList<LineItem> Items)
    : ICommand<Guid>;

// Command with no meaningful return value
public sealed record DeleteOrderCommand(Guid OrderId) : ICommand;
```

## Queries

Queries are read-only and idempotent, and require exactly one handler.

```csharp
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;
public sealed record ListOrdersQuery : IQuery<IReadOnlyList<OrderDto>>;
```

Only queries can be cached: implement `ICacheableQuery<T>` instead of `IQuery<T>`. See [Caching](/Vitreous/guides/caching/).

## Notifications

Notifications are fire-and-forget events dispatched to zero or more handlers.

```csharp
public sealed record OrderCreatedNotification(Guid OrderId) : INotification;
```

By default all handlers are invoked sequentially (fail-fast on first exception). Set `NotificationPublishStrategy = NotificationPublishStrategy.Parallel` in `VitreousOptions` to fan-out to all handlers concurrently via `Task.WhenAll` — every handler runs to completion regardless of individual failures, and all exceptions are collected into an `AggregateException`.

`Publish` dispatches on the notification's runtime type, like `Send`. See [Which handlers a notification reaches](/Vitreous/concepts/dispatching/#which-handlers-a-notification-reaches).

## Stream queries

Stream queries are read-only, lazy sequences that yield items over time, and require exactly one handler. Use them when data is too large to buffer, or when items should be consumed as they arrive.

```csharp
public sealed record GetOrderTicksQuery(int Count = 5) : IStreamQuery<OrderDto>;
```

Messages may be classes, records, or record structs, and may be nested inside other public or internal types. Next: [Handlers](/Vitreous/concepts/handlers/).
