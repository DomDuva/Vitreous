# Vitreous

[![Vitreous](https://img.shields.io/nuget/v/Vitreous.svg?label=Vitreous)](https://www.nuget.org/packages/Vitreous)
[![Downloads](https://img.shields.io/nuget/dt/Vitreous.svg?label=downloads)](https://www.nuget.org/packages/Vitreous)

[![SourceGeneration](https://img.shields.io/nuget/v/Vitreous.SourceGeneration.svg?label=SourceGeneration)](https://www.nuget.org/packages/Vitreous.SourceGeneration)
[![DevUI](https://img.shields.io/nuget/v/Vitreous.DevUI.svg?label=DevUI)](https://www.nuget.org/packages/Vitreous.DevUI)
[![Caching](https://img.shields.io/nuget/v/Vitreous.Caching.svg?label=Caching)](https://www.nuget.org/packages/Vitreous.Caching)
[![Caching.Redis](https://img.shields.io/nuget/v/Vitreous.Caching.Redis.svg?label=Caching.Redis)](https://www.nuget.org/packages/Vitreous.Caching.Redis)

A high-performance, source-generated mediator for .NET — a modern alternative to MediatR with zero runtime reflection, first-class caching, and a built-in developer UI. Supports .NET 8, .NET 9, and .NET 10.

**Packages**

| Package | Purpose |
|---|---|
| `Vitreous` | Core mediator: commands, queries, notifications, pipeline behaviors, Result type |
| `Vitreous.SourceGeneration` | Roslyn incremental generator — eliminates all runtime reflection |
| `Vitreous.DevUI` | Embedded developer dashboard: live traces, logs, cache hit indicators |
| `Vitreous.Caching` | Pipeline behaviors for `IDistributedCache`-backed query caching |
| `Vitreous.Caching.Redis` | Redis backend with tag-based bulk invalidation |

---

## Performance

Benchmarks run on .NET 9.0.14, AMD Ryzen 7 7800X3D. Source: [`benchmark/Vitreous.Benchmarks`](benchmark/Vitreous.Benchmarks).

MediatR is benchmarked at its default Transient handler lifetime. Switching to Singleton makes no meaningful difference to dispatch latency or allocation.

### Dispatch (hot path)

| Scenario | MediatR | Vitreous |
|---|---|---|
| Send query | 64.9 ns / 264 B | 57.9 ns / 64 B |
| Send void command | 65.0 ns / 192 B | 59.0 ns / 64 B |
| Publish notification | 88.8 ns / 288 B | 63.6 ns / **0 B** |
| Send query (+ 1 behavior) | 106.5 ns / 448 B | 106.1 ns / 240 B |
| Stream query (1 item) | 154.4 ns / 512 B | 99.3 ns / 184 B |
| Stream query (10 items) | 376.4 ns / 512 B | 201.1 ns / 184 B |

### Startup (`BuildServiceProvider`)

| | MediatR | Vitreous |
|---|---|---|
| Time | 37,404 ns | 1,191 ns |
| Allocated | 106,049 B | 7,536 B |

Vitreous is **~31× faster** to start and allocates **~14× less** — because `AddVitreousHandlers()` emits direct `TryAddSingleton` calls at build time with no assembly scanning or reflection.

---

## Table of contents

1. [Core package](#1-core-package)
   - [Installation](#installation)
   - [Messages](#messages)
   - [Handlers](#handlers)
   - [Dispatching](#dispatching)
   - [Stream query helpers](#stream-query-helpers)
   - [Pipeline behaviors](#pipeline-behaviors)
   - [Result type](#result-type)
   - [Error type](#error-type)
   - [Source generator](#source-generator)
2. [Developer UI](#2-developer-ui)
3. [Caching](#3-caching)
   - [IDistributedCache backend](#idistributedcache-backend)
   - [Redis backend](#redis-backend)
4. [Putting it all together](#4-putting-it-all-together)

---

## 1. Core package

### Installation

```
dotnet add package Vitreous
dotnet add package Vitreous.SourceGeneration
```

Register with the DI container:

```csharp
builder.Services.AddVitreousHandlers();
```

`AddVitreousHandlers()` is generated at build time by `Vitreous.SourceGeneration`. It discovers every handler in your project at compile time, registers them, and wires up a reflection-free dispatcher. No scanning, no startup overhead.

If you prefer to skip the source generator, call the reflection-based fallback instead:

```csharp
builder.Services.AddVitreous(); // registers ReflectionSender; handlers must be registered manually
```

Both overloads accept an optional `Action<VitreousOptions>` callback:

```csharp
builder.Services.AddVitreousHandlers(options =>
{
    options.DefaultHandlerLifetime = ServiceLifetime.Singleton; // default
});
```

---

### Messages

Vitreous has three kinds of messages.

**Commands** — mutate state, return a result, require exactly one handler.

```csharp
// Command with a return value
public sealed record CreateOrderCommand(Guid CustomerId, IReadOnlyList<LineItem> Items)
    : ICommand<Guid>;

// Command with no meaningful return value
public sealed record DeleteOrderCommand(Guid OrderId) : ICommand;
```

**Queries** — read-only, idempotent, require exactly one handler.

```csharp
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;
public sealed record ListOrdersQuery : IQuery<IReadOnlyList<OrderDto>>;
```

**Notifications** — fire-and-forget events dispatched to zero or more handlers.

```csharp
public sealed record OrderCreatedNotification(Guid OrderId) : INotification;
```

**Stream queries** — read-only, lazy sequences that yield items over time; require exactly one handler. Use when data is too large to buffer, or when items should be consumed as they arrive.

```csharp
public sealed record GetOrderTicksQuery(int Count = 5) : IStreamQuery<OrderDto>;
```

---

### Handlers

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

The source generator emits a diagnostic if a command or query has no handler (`VIT001`, warning) or more than one handler (`VIT002`, error).

#### Stream query handlers

Stream queries have two handler patterns.

**Pattern 1 — `StreamQueryHandler<TQuery, TResult>` base class (recommended for most cases)**

Override the single `Stream` method and use `yield return` freely. The base class wraps it in a successful `Result` automatically.

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

**Pattern 2 — `IStreamQueryHandler<TQuery, TResult>` (when validation is required)**

C# does not allow `yield return` in a method that returns `ValueTask<…>`, so split the handler into two methods: `Handle` for setup/validation and a private iterator for item production. Return a failure `Result` from `Handle` to signal errors before any item is produced.

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

---

### Dispatching

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

#### Stream queries

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

---

### Stream query helpers

`StreamQueryExtensions` provides ergonomic helpers for working with the `Result<IAsyncEnumerable<T>>` returned by `CreateStream`.

#### `CollectStream` — eager collection

Opens the stream and awaits all items into a `List<T>`. Useful for tests and one-shot scenarios where the full result is needed at once. Captures both setup failures and mid-stream exceptions.

```csharp
Result<List<OrderDto>> result = await sender.CollectStream(new GetOrderTicksQuery(3), ct);
```

#### `MapStream` — lazy projection

Projects each item with a synchronous mapper. Equivalent to LINQ `Select` — no buffering.

```csharp
Result<IAsyncEnumerable<string>> names = (await sender.CreateStream(query, ct)).MapStream(o => o.CustomerName);
```

#### `FilterStream` — lazy filtering

Keeps only items satisfying a predicate. Equivalent to LINQ `Where` — no buffering.

```csharp
Result<IAsyncEnumerable<OrderDto>> large = (await sender.CreateStream(query, ct)).FilterStream(o => o.Quantity > 10);
```

#### `TakeStream` — lazy truncation

Stops the stream after at most `count` items. Equivalent to LINQ `Take` — no buffering.

```csharp
Result<IAsyncEnumerable<OrderDto>> first3 = (await sender.CreateStream(query, ct)).TakeStream(3);
```

All three helpers pass failures through untouched — if `CreateStream` returned a failure `Result`, the helper returns the same failure without opening the stream.
```

---

### Pipeline behaviors

Pipeline behaviors wrap every dispatch, similar to middleware. Register them as open-generic services after `AddVitreousHandlers()`.

```csharp
// One registration covers commands (void and non-void), queries, and their full pipeline.
builder.Services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

Behaviors execute outermost-first (first registered = outermost wrapper).

```csharp
public sealed class LoggingBehavior<TMessage, TResult>(ILogger<LoggingBehavior<TMessage, TResult>> logger)
    : IPipelineBehavior<TMessage, TResult>
{
    public async ValueTask<Result<TResult>> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        logger.LogInformation("Handling {MessageType}", typeof(TMessage).Name);
        var result = await next(message, ct);
        logger.LogInformation("Handled {MessageType} — {Outcome}",
            typeof(TMessage).Name, result.IsSuccess ? "success" : "failure");
        return result;
    }
}
```

#### Void commands and `Unit`

Void commands (`ICommand`) participate in the same `IPipelineBehavior<TMessage, TResult>` pipeline. When behaviors are present, `TResult` is resolved as `Unit` and the handler's `Result` is adapted before the chain runs and converted back on the way out. When no behaviors are registered the handler is called directly with no `Unit` conversion or async state machine overhead.

| Message kind | `TMessage` | `TResult` | Covered by `IPipelineBehavior<,>`? |
|---|---|---|---|
| Command with value | `ICommand<TResult>` | e.g. `Guid` | Yes |
| Void command | `ICommand` | `Unit` | Yes |
| Query | `IQuery<TResult>` | e.g. `OrderDto` | Yes |
| Notification | `INotification` | — | No — use `INotificationPipelineBehavior<TNotification>` |
| Stream query | `IStreamQuery<TResult>` | — | No — use `IStreamPipelineBehavior<TMessage, TResult>` |

Notifications dispatch to multiple handlers with a fan-out model and no `Result` wrapper, so they remain on their own interface.

```csharp
builder.Services.AddSingleton(
    typeof(INotificationPipelineBehavior<>), typeof(NotificationLoggingBehavior<>));
```

#### Stream pipeline behaviors

Stream queries have their own `IStreamPipelineBehavior<TMessage, TResult>` interface. Behaviors run before the stream is opened and can short-circuit with a failure `Result` before any item is produced.

```csharp
builder.Services.AddSingleton(
    typeof(IStreamPipelineBehavior<,>), typeof(StreamAuthorizationBehavior<,>));
```

```csharp
public sealed class StreamAuthorizationBehavior<TMessage, TResult>(ICurrentUser user)
    : IStreamPipelineBehavior<TMessage, TResult>
    where TMessage : IStreamQuery<TResult>
{
    public ValueTask<Result<IAsyncEnumerable<TResult>>> Handle(
        TMessage message,
        StreamQueryHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        if (!user.IsAuthenticated)
            return new(Result<IAsyncEnumerable<TResult>>.Fail(Error.Unauthorized("Authentication required.")));

        return next(message, ct);
    }
}
```

---

### Result type

Every handler returns `Result` or `Result<T>`. Results are either a success value or an `Error` — never both.

```csharp
// Implicit conversions (the most common path)
Result<Guid> ok  = Guid.NewGuid();                     // T → Result<T>
Result<Guid> err = Error.NotFound("Order not found."); // Error → Result<T>

// Branch on outcome
string message = result.Match(
    id  => $"Created order {id}",
    err => $"Error: {err.Message}"
);

// Transform the success value
Result<string> idString = result.Map(id => id.ToString());

// Chain a fallible operation
Result<OrderDto> dto = result.Bind(id => LoadOrder(id));

// Async variants
string message = await result.MatchAsync(
    async id  => await FormatOrderAsync(id),
    async err => err.Message
);
```

`Unit` is a built-in struct that represents the absence of a value. It is used as `TResult` in behaviors when a void command is dispatched — you will not use it in handlers directly, but may encounter it in generic behavior constraints.

---

### Error type

`Error` is a sealed record carrying a `Code` and `Message`, with optional extension data.

```csharp
Error.NotFound("Order 123 not found.")
Error.Validation("Invalid request.", failures: new Dictionary<string, string[]>
{
    ["customerId"] = ["Customer ID is required."],
    ["items"]      = ["At least one item is required."]
})
Error.Unauthorized("You do not have access to this resource.")
Error.Conflict("An order for this customer is already pending.")
Error.Internal("Unexpected failure.", exception: ex)
Error.Custom("PAYMENT_DECLINED", "Payment was declined by the processor.")
```

All factory methods accept an optional `code` override to replace the default code string.

---

### Source generator

`Vitreous.SourceGeneration` is a build-time Roslyn incremental generator referenced as `PrivateAssets="all"` — it produces no runtime dependency. It scans your project's compilation and emits:

- **`VitreousServiceCollectionExtensions.g.cs`** — the `AddVitreousHandlers()` extension method that registers every discovered handler and replaces the reflection-based sender with the generated dispatcher.
- **`VitreousDispatcher.g.cs`** — a concrete `ISender` / `IPublisher` implementation that dispatches via `if/else` chains on concrete types with no reflection.

No attributes, no startup overhead. The generator runs during `dotnet build` and produces errors or warnings in your IDE when handler contracts are violated.

#### Diagnostics

| ID | Severity | Trigger |
|---|---|---|
| `VIT001` | Warning | A command, query, or stream query has no registered handler |
| `VIT002` | Error | A command, query, or stream query has more than one handler |
| `VIT003` | Warning | A `IAsyncEnumerable`-returning method inside a stream query handler is missing `[EnumeratorCancellation]` on its `CancellationToken` parameter — callers using `.WithCancellation(ct)` will not be able to cancel mid-stream |

#### Multi-project solutions (DDD, Clean Architecture)

The generator automatically scans both the current project's source files **and** every referenced assembly that directly references `Vitreous`. Handlers can live in separate projects with no extra configuration.

```
MyApp.Api              ← install Vitreous.SourceGeneration here only
  ├── ref MyApp.Application    (commands, queries, handlers)
  └── ref MyApp.Infrastructure (repository handlers, integrations)
```

Application and infrastructure projects — core package only:

```
dotnet add package Vitreous
```

Entry-point project only:

```
dotnet add package Vitreous
dotnet add package Vitreous.SourceGeneration
```

```csharp
// MyApp.Api / Program.cs — unchanged, handlers from all referenced projects are discovered
builder.Services.AddVitreousHandlers();
```

> **Important:** install `Vitreous.SourceGeneration` **only on the entry-point project** (the one that contains `Program.cs` and calls `AddVitreousHandlers()`). Installing it on handler projects is unnecessary and causes a `VitreousDispatcher` naming conflict.

---

### Registration methods

Vitreous provides two ways to register handlers. They are **mutually exclusive** — choose one.

#### `AddVitreousHandlers()` — source-generated *(recommended)*

Zero runtime reflection. The generator emits a concrete dispatcher at build time. Requires `Vitreous.SourceGeneration` on the entry-point project.

#### `AddVitreousHandlers(params Assembly[])` — reflection-based

Scans the supplied assemblies at application startup. No source generator needed. Useful for plugin architectures or when compile-time generation is not available.

```csharp
builder.Services.AddVitreousHandlers(
    typeof(CreateOrderHandler).Assembly,
    typeof(SqlOrderRepository).Assembly
);
```

| | `AddVitreousHandlers()` | `AddVitreousHandlers(assemblies)` |
|---|---|---|
| Discovery | Build time | Startup (reflection) |
| Runtime reflection | None | Yes |
| AOT compatible | Yes | No |
| Multi-project | Automatic | Manual — specify each assembly |
| Dispatcher | `VitreousDispatcher` (generated) | `ReflectionSender` |
| Best for | Most applications | Plugin architectures, dynamic loading |

---

## 2. Developer UI

`Vitreous.DevUI` embeds a live trace dashboard into your application — visible only in development.

### Installation

```
dotnet add package Vitreous.DevUI
```

```csharp
// Program.cs
if (builder.Environment.IsDevelopment())
    builder.Services.AddVitreousDevUI(options =>
    {
        options.PathPrefix          = "/vitreous"; // URL where the UI is mounted
        options.TraceBufferCapacity = 500;          // number of traces kept in memory
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseVitreousDevUI();
```

Navigate to `http://localhost:{port}/vitreous` (or your configured `PathPrefix`) to open the dashboard.

### What it captures

Every command, query, and stream query dispatch is traced automatically — no attributes or manual instrumentation required. Each trace includes:

| Field | Description |
|---|---|
| Message type | Simple and fully-qualified type name |
| Category | `command`, `query`, `notification`, or `streamquery` |
| Duration | Full wall-clock time including all pipeline behaviors |
| Outcome | `Success` or `Failure`, with error code and message on failure |
| Cache hit | Marked as `Cached` when `Vitreous.Caching` short-circuits the handler |
| Input | JSON-serialized request message |
| Output | JSON-serialized success value |
| Logs | All `ILogger` calls emitted inside the handler during the dispatch |
| Timestamp | Wall-clock time when the dispatch began |

Stream query traces include additional fields:

| Field | Description |
|---|---|
| Stream phase | `Opened` (stream is live and yielding items) or `Closed` (stream completed or failed) |
| Item count | Number of items yielded before the stream closed |
| Live duration | A ticking elapsed-time counter is shown in the UI while the stream is open |

### How it works

`AddVitreousDevUI()` inserts `TracingBehavior<,>` (for commands and queries) and `StreamTracingBehavior<,>` (for stream queries) as the **outermost** pipeline behaviors regardless of what else is registered, so every dispatch — including cache hits from inner behaviors — is always captured. Log capture is implemented via a custom `ILoggerProvider` backed by an `AsyncLocal`, so log entries are attributed to the correct dispatch even under concurrent requests.

Stream dispatch emits two SSE events: `Opened` (when the stream starts yielding) and `Closed` (when it completes or errors). The dashboard shows a live ticking duration counter while the stream is open and freezes it on `Closed`.

The UI connects over Server-Sent Events (`/vitreous/events`). On page load it replays the full in-memory ring buffer, then streams new traces live. No WebSocket, no polling.

### Endpoints

| Endpoint | Description |
|---|---|
| `GET {prefix}` | The single-file dashboard UI |
| `GET {prefix}/events` | SSE stream (replay + live) |
| `GET {prefix}/api/traces` | JSON snapshot of the ring buffer |

---

## 3. Caching

Vitreous provides two caching packages. Use `Vitreous.Caching` for any `IDistributedCache` backend (in-memory, SQL, custom). Use `Vitreous.Caching.Redis` for Redis with tag-based bulk invalidation.

### IDistributedCache backend

```
dotnet add package Vitreous.Caching
```

```csharp
// Register a cache store (your choice of backend)
builder.Services.AddDistributedMemoryCache();           // in-process, single-node
// or: builder.Services.AddStackExchangeRedisCache(...) // IDistributedCache over Redis

// Register the Vitreous caching behaviors
builder.Services.AddVitreousCaching(options =>
{
    options.KeyPrefix = "vitreous:"; // default — prepended to every cache key
});
```

#### Making a query cacheable

Implement `ICacheableQuery<TResult>` instead of `IQuery<TResult>`:

```csharp
public sealed record GetOrderQuery(Guid OrderId) : ICacheableQuery<OrderDto>
{
    public string CacheKey              => $"order:{OrderId}";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration  => null;
}
```

- `CacheKey` must uniquely represent the query — include every parameter that influences the result.
- `AbsoluteExpiration` takes precedence over `SlidingExpiration` if both are set.
- `SlidingExpiration` resets the TTL on each cache hit. Not supported by the Redis backend (see below).
- The handler interface (`IQueryHandler<,>`) is unchanged.

#### Invalidating cache entries after a command

Implement `IInvalidatesCache` on any command:

```csharp
public sealed record CreateOrderCommand(Guid CustomerId) : ICommand<Guid>, IInvalidatesCache
{
    IReadOnlyList<string> IInvalidatesCache.CacheKeys       => ["orders:list"];
    IReadOnlyList<string> IInvalidatesCache.InvalidatesTags => []; // tags: Redis backend only
}
```

Invalidation only runs when the command returns a success result. Cache removal failures are logged as warnings and do not fail the command.

> **Note:** `IInvalidatesCache` works on void commands (`ICommand`) too — no need to use `ICommand<bool>` purely for cache invalidation.

#### Behavior pipeline order

```
TracingBehavior          ← DevUI, always outermost
  └─ LoggingBehavior     ← your custom behaviors
       └─ CacheInvalidationBehavior
            └─ CachingBehavior   ← innermost; returns cached value before the handler is called
                 └─ handler
```

---

### Redis backend

```
dotnet add package Vitreous.Caching.Redis
```

```csharp
builder.Services.AddVitreousRedisCaching(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    options.KeyPrefix        = "vitreous:"; // default
    options.TagPrefix        = "vtag:";     // default — prefix for Redis Sets used in tag tracking
});
```

Do **not** call `AddVitreousCaching()` alongside `AddVitreousRedisCaching()` — the Redis package is self-contained and registers everything internally.

#### Tag-based invalidation

Tags allow invalidating many cache entries at once without knowing every key individually.

```csharp
public sealed record GetOrderQuery(Guid OrderId) : ICacheableQuery<OrderDto>
{
    public string CacheKey              => $"order:{OrderId}";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration  => null;
    public IReadOnlyList<string> Tags   => ["orders", $"order:{OrderId}"];
}

// Invalidates the "orders" tag — removes every key stored under it
public sealed record CreateOrderCommand(Guid CustomerId) : ICommand<Guid>, IInvalidatesCache
{
    IReadOnlyList<string> IInvalidatesCache.CacheKeys       => ["orders:list"];
    IReadOnlyList<string> IInvalidatesCache.InvalidatesTags => ["orders"];
}
```

Internally, each tag is a Redis Set. When a value is written, its key is added to every tag Set. `RemoveByTagAsync` runs a Lua script that reads the Set, deletes every member key, and deletes the Set itself — atomically on a single node.

> **Redis Cluster:** the Lua script deletes keys discovered at runtime, which is not compatible with Redis Cluster if tagged keys are distributed across multiple hash slots. Use hash tags in your key names (e.g. `{order}:123`) to colocate related keys on the same slot.

#### Sliding expiration

`SlidingExpiration` is not enforced by the Redis backend. If `AbsoluteExpiration` is `null`, the `SlidingExpiration` value is used as a fixed TTL (not a true sliding window). Use the `IDistributedCache` backend if you need genuine sliding expiration semantics.

---

## 4. Putting it all together

```csharp
var builder = WebApplication.CreateBuilder(args);

// Core mediator (source-generated dispatcher + handler discovery)
builder.Services.AddVitreousHandlers();

// Custom pipeline behavior
builder.Services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// Caching — choose one:
builder.Services.AddDistributedMemoryCache();
builder.Services.AddVitreousCaching();
// or: builder.Services.AddVitreousRedisCaching(options =>
// {
//     options.ConnectionString = builder.Configuration.GetConnectionString("Redis");
// });

// Developer UI — development only
if (builder.Environment.IsDevelopment())
    builder.Services.AddVitreousDevUI();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseVitreousDevUI();

app.MapPost("/orders", async (CreateOrderCommand cmd, ISender sender) =>
    (await sender.Send(cmd)).Match(
        id  => Results.Created($"/orders/{id}", new { id }),
        err => Results.Problem(err.Message)));

app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender) =>
    (await sender.Send(new GetOrderQuery(id))).Match(
        order => Results.Ok(order),
        err   => Results.Problem(err.Message)));

app.Run();
```

---

## License

MIT
