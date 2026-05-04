# Vitreous

[![NuGet Version](https://img.shields.io/nuget/v/Vitreous.svg)](https://www.nuget.org/packages/Vitreous)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Vitreous.svg)](https://www.nuget.org/packages/Vitreous)

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

## Table of contents

1. [Core package](#1-core-package)
   - [Installation](#installation)
   - [Messages](#messages)
   - [Handlers](#handlers)
   - [Dispatching](#dispatching)
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

```xml
<!-- Replace 0.1.0 with the latest version from https://www.nuget.org/packages/Vitreous -->
<PackageReference Include="Vitreous" Version="0.1.0" />

<!-- Replace 0.1.0 with the latest version from https://www.nuget.org/packages/Vitreous -->
<PackageReference Include="Vitreous.SourceGeneration" Version="0.1.0" PrivateAssets="all" />
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

Void commands (`ICommand`) participate in the **same** `IPipelineBehavior<TMessage, TResult>` pipeline. When a void command is dispatched, `TResult` is resolved as `Unit` — a built-in struct that represents the absence of a meaningful value. The `PipelineExecutor` adapts the handler's `Result` to `Result<Unit>` before the behavior chain runs and converts it back on the way out, so behaviors see a uniform `Result<TResult>` in both cases.

This means a single open-generic registration covers everything:

| Message kind | `TMessage` | `TResult` | Covered by `IPipelineBehavior<,>`? |
|---|---|---|---|
| Command with value | `ICommand<TResult>` | e.g. `Guid` | Yes |
| Void command | `ICommand` | `Unit` | Yes |
| Query | `IQuery<TResult>` | e.g. `OrderDto` | Yes |
| Notification | `INotification` | — | No — use `INotificationPipelineBehavior<TNotification>` |

Notifications dispatch to multiple handlers with a fan-out model and no `Result` wrapper, so they remain on their own interface.

```csharp
// Notification behaviors — registered separately
builder.Services.AddSingleton(
    typeof(INotificationPipelineBehavior<>), typeof(NotificationLoggingBehavior<>));
```

---

### Result type

Every handler returns `Result` or `Result<T>`. Results are either a success value or an `Error` — never both.

The special type `Unit` represents the absence of a value and is used as `TResult` in behaviors when a void command is dispatched. You do not use `Unit` directly in handlers — handlers still return `Result` — but you may encounter it in generic behavior constraints.

```csharp
// Create
Result<Guid>  ok  = Result<Guid>.Ok(Guid.NewGuid());
Result<Guid>  err = Result<Guid>.Fail(Error.NotFound("Order not found."));

// Implicit conversions (the most common path)
Result<Guid> ok2  = Guid.NewGuid();                     // T → Result<T>
Result<Guid> err2 = Error.NotFound("Order not found."); // Error → Result<T>

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

#### Multi-project solutions (DDD, Clean Architecture)

The generator automatically scans both the current project's source files **and** every referenced assembly that directly references `Vitreous`. This means handlers can live in separate projects with no extra configuration.

```
MyApp.Api              ← install Vitreous.SourceGeneration here only
  ├── ref MyApp.Application    (commands, queries, handlers)
  └── ref MyApp.Infrastructure (repository handlers, integrations)
```

```xml
<!-- MyApp.Application and MyApp.Infrastructure — no source generator needed -->
<!-- Replace 0.1.0 with the latest version from https://www.nuget.org/packages/Vitreous -->
<PackageReference Include="Vitreous" Version="0.1.0" />

<!-- MyApp.Api only -->
<!-- Replace 0.1.0 with the latest version from https://www.nuget.org/packages/Vitreous -->
<PackageReference Include="Vitreous" Version="0.1.0" />
<PackageReference Include="Vitreous.SourceGeneration" Version="0.1.0" PrivateAssets="all" />
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

```csharp
builder.Services.AddVitreousHandlers();

// With options
builder.Services.AddVitreousHandlers(options =>
{
    options.DefaultHandlerLifetime = ServiceLifetime.Singleton; // default
});
```

#### `AddVitreousHandlers(params Assembly[])` — reflection-based

Scans the supplied assemblies at application startup. No source generator needed. Uses `ReflectionSender` at runtime. Useful for plugin architectures or when compile-time generation is not available.

```csharp
// Specify every assembly that contains handlers
builder.Services.AddVitreousHandlers(
    typeof(CreateOrderHandler).Assembly,   // MyApp.Application
    typeof(SqlOrderRepository).Assembly    // MyApp.Infrastructure
);

// With options
builder.Services.AddVitreousHandlers(
    opt => opt.DefaultHandlerLifetime = ServiceLifetime.Scoped,
    typeof(CreateOrderHandler).Assembly,
    typeof(SqlOrderRepository).Assembly
);
```

#### Comparison

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

```xml
<!-- Replace 0.1.0 with the latest version from https://www.nuget.org/packages/Vitreous.DevUI -->
<PackageReference Include="Vitreous.DevUI" Version="0.1.0" />
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

Every command and query dispatch is traced automatically — no attributes or manual instrumentation required. Each trace includes:

| Field | Description |
|---|---|
| Message type | Simple and fully-qualified type name |
| Category | `Command`, `Query`, or `Notification` |
| Duration | Full wall-clock time including all pipeline behaviors |
| Outcome | `Success` or `Failure`, with error code and message on failure |
| Cache hit | Marked as `Cached` when `Vitreous.Caching` short-circuits the handler |
| Input | JSON-serialized request message |
| Output | JSON-serialized success value |
| Logs | All `ILogger` calls emitted inside the handler during the dispatch |
| Timestamp | Wall-clock time when the dispatch began |

### How it works

`AddVitreousDevUI()` inserts `TracingBehavior<,>` as the **outermost** pipeline behavior regardless of what else is registered, so every dispatch — including cache hits from inner behaviors — is always captured. Log capture is implemented via a custom `ILoggerProvider` backed by an `AsyncLocal`, so log entries are attributed to the correct dispatch even under concurrent requests.

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

```xml
<!-- Replace 0.1.0 with the latest version from https://www.nuget.org/packages/Vitreous.Caching -->
<PackageReference Include="Vitreous.Caching" Version="0.1.0" />
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

public sealed record ListOrdersQuery : ICacheableQuery<IReadOnlyList<OrderDto>>
{
    public string CacheKey              => "orders:list";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(2);
    public TimeSpan? SlidingExpiration  => null;
}
```

- `CacheKey` must uniquely represent the query. Include every parameter that influences the result.
- `AbsoluteExpiration` takes precedence over `SlidingExpiration` if both are set.
- `SlidingExpiration` resets the TTL on each cache hit. Not supported by the Redis backend (see below).
- The handler interface (`IQueryHandler<,>`) is unchanged — the query type simply changes its base interface.

#### Invalidating cache entries after a command

Implement `IInvalidatesCache` on any command:

```csharp
public sealed record CreateOrderCommand(Guid CustomerId) : ICommand<Guid>, IInvalidatesCache
{
    IReadOnlyList<string> IInvalidatesCache.CacheKeys       => ["orders:list"];
    IReadOnlyList<string> IInvalidatesCache.InvalidatesTags => []; // tags: Redis backend only
}

public sealed record DeleteOrderCommand(Guid OrderId) : ICommand<bool>, IInvalidatesCache
{
    IReadOnlyList<string> IInvalidatesCache.CacheKeys       => [$"order:{OrderId}", "orders:list"];
    IReadOnlyList<string> IInvalidatesCache.InvalidatesTags => [];
}
```

Invalidation only runs when the command returns a success result. Cache removal failures are logged as warnings and do not fail the command.

> **Note on `ICommand` vs `ICommand<bool>`:** `CacheInvalidationBehavior` is typed as `IPipelineBehavior<TMessage, TResult>`. Void commands (`ICommand`) now also participate in this pipeline with `TResult = Unit`, so `IInvalidatesCache` works on void commands too — no need to make the command `ICommand<bool>` purely for cache invalidation.

#### Behavior pipeline order

When registered together, the pipeline stack (outermost to innermost) is:

```
TracingBehavior          ← DevUI, always outermost
  └─ LoggingBehavior     ← your custom behaviors
       └─ CacheInvalidationBehavior
            └─ CachingBehavior   ← innermost; returns cached value before the handler is called
                 └─ handler
```

Tracing and logging always execute, even on cache hits.

---

### Redis backend

```xml
<!-- Replace 0.1.0 with the latest version from https://www.nuget.org/packages/Vitreous.Caching.Redis -->
<PackageReference Include="Vitreous.Caching.Redis" Version="0.1.0" />
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

The Redis backend adds support for the `Tags` property on `ICacheableQuery<TResult>` and `InvalidatesTags` on `IInvalidatesCache`. Tags allow invalidating many cache entries at once without knowing every key individually.

```csharp
public sealed record GetOrderQuery(Guid OrderId) : ICacheableQuery<OrderDto>
{
    public string CacheKey              => $"order:{OrderId}";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration  => null;

    // Stored under the "orders" and "order:{id}" tags
    public IReadOnlyList<string> Tags   => ["orders", $"order:{OrderId}"];
}

public sealed record ListOrdersQuery : ICacheableQuery<IReadOnlyList<OrderDto>>
{
    public string CacheKey              => "orders:list";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(2);
    public TimeSpan? SlidingExpiration  => null;
    public IReadOnlyList<string> Tags   => ["orders"];
}

// Invalidates the "orders" tag — removes every key that was stored under it
public sealed record CreateOrderCommand(Guid CustomerId) : ICommand<Guid>, IInvalidatesCache
{
    IReadOnlyList<string> IInvalidatesCache.CacheKeys       => ["orders:list"];
    IReadOnlyList<string> IInvalidatesCache.InvalidatesTags => ["orders"];
}

public sealed record DeleteOrderCommand(Guid OrderId) : ICommand<bool>, IInvalidatesCache
{
    IReadOnlyList<string> IInvalidatesCache.CacheKeys       => [$"order:{OrderId}", "orders:list"];
    IReadOnlyList<string> IInvalidatesCache.InvalidatesTags => ["orders"];
}
```

Internally, each tag is a Redis Set. When a value is written, its key is added to every tag Set. `RemoveByTagAsync` runs a Lua script that reads the Set, deletes every member key, and deletes the Set itself — atomically on a single node.

> **Redis Cluster:** the Lua script deletes keys discovered at runtime, which is not compatible with Redis Cluster if tagged keys are distributed across multiple hash slots. Use hash tags in your key names (e.g. `{order}:123`) to colocate related keys on the same slot.

#### Sliding expiration

`SlidingExpiration` on `ICacheableQuery<TResult>` is not enforced by the Redis backend. If `AbsoluteExpiration` is `null`, the `SlidingExpiration` value is used as a fixed TTL for the Redis entry (not a true sliding window). Use the `IDistributedCache` backend if you need genuine sliding expiration semantics.

---

## 4. Putting it all together

A minimal `Program.cs` using every package:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Core mediator (source-generated dispatcher + handler discovery)
builder.Services.AddVitreousHandlers();

// Custom pipeline behavior (registered before caching so it wraps everything except tracing)
builder.Services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// Caching — choose one of the following:
//   IDistributedCache backend (in-process):
builder.Services.AddDistributedMemoryCache();
builder.Services.AddVitreousCaching();

//   Redis backend (comment out the two lines above and use this instead):
// builder.Services.AddVitreousRedisCaching(options =>
// {
//     options.ConnectionString = builder.Configuration.GetConnectionString("Redis");
// });

// Developer UI — development only
if (builder.Environment.IsDevelopment())
    builder.Services.AddVitreousDevUI();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseVitreousDevUI();

// Map your endpoints
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

With this setup:

- Every dispatch goes through `TracingBehavior → LoggingBehavior → CacheInvalidationBehavior → CachingBehavior → handler`
- Query results are cached; cache hits skip the handler and are marked as `Cached` in the DevUI
- Commands that implement `IInvalidatesCache` automatically evict stale entries on success
- The developer dashboard is available at `http://localhost:{port}/vitreous`

---

## License

MIT
