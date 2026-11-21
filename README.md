# Vitreous

[![Vitreous](https://img.shields.io/nuget/v/Vitreous.svg?label=Vitreous)](https://www.nuget.org/packages/Vitreous)
[![Downloads](https://img.shields.io/nuget/dt/Vitreous.svg?label=downloads)](https://www.nuget.org/packages/Vitreous)

[![SourceGeneration](https://img.shields.io/nuget/v/Vitreous.SourceGeneration.svg?label=SourceGeneration)](https://www.nuget.org/packages/Vitreous.SourceGeneration)
[![DevUI](https://img.shields.io/nuget/v/Vitreous.DevUI.svg?label=DevUI)](https://www.nuget.org/packages/Vitreous.DevUI)
[![Caching](https://img.shields.io/nuget/v/Vitreous.Caching.svg?label=Caching)](https://www.nuget.org/packages/Vitreous.Caching)
[![Caching.Redis](https://img.shields.io/nuget/v/Vitreous.Caching.Redis.svg?label=Caching.Redis)](https://www.nuget.org/packages/Vitreous.Caching.Redis)

A high-performance, source-generated mediator for .NET — a modern alternative to MediatR with zero runtime reflection, first-class caching, and a built-in developer UI. Supports .NET 8, 9, 10 and 11.

**Packages**

| Package | Purpose |
|---|---|
| `Vitreous` | Core mediator: commands, queries, notifications, pipeline behaviors, Result type |
| `Vitreous.SourceGeneration` | Roslyn incremental generator — eliminates all runtime reflection |
| `Vitreous.DevUI` | Embedded developer dashboard: live traces, logs, cache hit indicators |
| `Vitreous.Caching` | Pipeline behaviors for `IDistributedCache`-backed query caching |
| `Vitreous.Caching.Redis` | Redis backend with tag-based bulk invalidation |

---

## Quick start

Three steps from an empty project to a working dispatch.

**1. Install** (in the project that contains `Program.cs`):

```
dotnet add package Vitreous
dotnet add package Vitreous.SourceGeneration
```

**2. Define a message and its handler** anywhere in your project:

```csharp
using Vitreous;
using Vitreous.Abstractions;

public sealed record CreateOrderCommand(string Customer) : ICommand<Guid>;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Guid>
{
    public ValueTask<Result<Guid>> Handle(CreateOrderCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Customer))
            return new(Error.Validation("Customer is required."));

        return new(Guid.NewGuid()); // T converts to Result<T> implicitly
    }
}
```

**3. Register and dispatch:**

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddVitreousHandlers(); // generated at build time — finds every handler

var app = builder.Build();

app.MapPost("/orders", async (CreateOrderCommand cmd, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(cmd, ct);
    return result.Match(
        id  => Results.Created($"/orders/{id}", new { id }),
        err => Results.BadRequest(new { err.Code, err.Message }));
});

app.Run();
```

That's it. No attributes, no assembly scanning, and the compiler warns you (`VIT001`) if a command or query has no handler.

> **No source generator?** Call `AddVitreousHandlers(typeof(Program).Assembly)` instead and handlers are discovered by reflection at startup. See [Registration methods](#registration-methods).

---

## Performance

Benchmarks run on .NET 11.0.0, Intel Xeon Silver 4114 (2.2 GHz). Source: [`benchmark/Vitreous.Benchmarks`](benchmark/Vitreous.Benchmarks).

MediatR is benchmarked at its default Transient handler lifetime; Vitreous with `DefaultHandlerLifetime = Singleton`. Switching MediatR to Singleton makes no meaningful difference to dispatch latency or allocation.

### Dispatch (hot path)

| Scenario | MediatR | Vitreous |
|---|---|---|
| Send query | 159.2 ns / 192 B | 102.0 ns / **0 B** |
| Send query (200+ handlers registered) | 146.9 ns / 192 B | 109.1 ns / **0 B** |
| Send void command | 166.6 ns / 120 B | 86.9 ns / **0 B** |
| Publish notification | 224.0 ns / 272 B | 194.4 ns / **0 B** |
| Send query (+ 1 behavior) | 319.6 ns / 368 B | 128.2 ns / 56 B |
| Stream query (1 item) | 401.5 ns / 496 B | 186.2 ns / 120 B |
| Stream query (10 items) | 898.9 ns / 496 B | 434.5 ns / 120 B |

Dispatch is one lookup on the message's concrete type, so its cost does not grow with the number of registered handlers.

### Startup (`BuildServiceProvider`)

The benchmark project registers 200+ handlers per library.

| | MediatR | Vitreous |
|---|---|---|
| Time | 10,061,341 ns | 67,193 ns |
| Allocated | 2,893,031 B | 58,416 B |

Vitreous is **~150× faster** to start and allocates **~50× less** — because `AddVitreousHandlers()` emits direct registration calls at build time with no assembly scanning or reflection.

---

## Table of contents

0. [Quick start](#quick-start)
1. [Core package](#1-core-package)
   - [Installation](#installation)
   - [Messages](#messages)
   - [Handlers](#handlers)
   - [Dispatching](#dispatching)
   - [Stream query helpers](#stream-query-helpers)
   - [Pipeline behaviors](#pipeline-behaviors)
   - [OpenTelemetry](#opentelemetry)
   - [Result type](#result-type)
   - [Error type](#error-type)
   - [Source generator](#source-generator)
2. [Developer UI](#2-developer-ui)
3. [Caching](#3-caching)
   - [IDistributedCache backend](#idistributedcache-backend)
   - [Redis backend](#redis-backend)
4. [Putting it all together](#4-putting-it-all-together)
5. [Troubleshooting](#5-troubleshooting)

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

If you prefer to skip the source generator, pass the assemblies that contain your handlers and they are discovered by reflection at startup:

```csharp
builder.Services.AddVitreousHandlers(typeof(Program).Assembly);
```

Calling `AddVitreousHandlers()` with no arguments and without the source generator installed throws at startup with a message explaining both options — it never silently registers nothing.

Both overloads accept an optional `Action<VitreousOptions>` callback:

```csharp
builder.Services.AddVitreousHandlers(options =>
{
    options.DefaultHandlerLifetime      = ServiceLifetime.Scoped;                // default
    options.NotificationPublishStrategy = NotificationPublishStrategy.Sequential; // default; set to Parallel for concurrent fan-out
});
```

`DefaultHandlerLifetime` applies to every discovered handler. The default, `Scoped`, is safe for handlers that depend on scoped services such as a `DbContext`. `ISender` and `IPublisher` are scoped unless the lifetime is `Singleton`, so resolve them from a scope: minimal APIs and controllers do this automatically, while background services should create one with `IServiceScopeFactory`. Use `Singleton` only when every handler is stateless and has no scoped dependencies.

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

By default all handlers are invoked sequentially (fail-fast on first exception). Set `NotificationPublishStrategy = NotificationPublishStrategy.Parallel` in `VitreousOptions` to fan-out to all handlers concurrently via `Task.WhenAll` — every handler runs to completion regardless of individual failures, and all exceptions are collected into an `AggregateException`.

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

### OpenTelemetry

Vitreous reports every dispatch through `System.Diagnostics`, so there is no extra package to install. Subscribe to its activity source and meters:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(VitreousTelemetry.ActivitySourceName))   // "Vitreous"
    .WithMetrics(m => m.AddMeter(VitreousTelemetry.MeterName,              // "Vitreous"
                                 VitreousCachingTelemetry.MeterName));     // "Vitreous.Caching"
```

Nothing is recorded until a listener subscribes. With no listener, a dispatch takes the same path as before and still allocates nothing. With a listener that records every span and measurement, a send costs about 0.7 µs and 576 B more (.NET 11, `TelemetryBenchmarks`), most of it the `Activity` itself.

**Spans** (`ActivityKind.Internal`, a child of the current span, e.g. the ASP.NET Core request):

| Span name | Started by |
|---|---|
| `vitreous command {Message}` | `Send(ICommand<T>)`, `Send(ICommand)` |
| `vitreous query {Message}` | `Send(IQuery<T>)` |
| `vitreous stream {Message}` | `CreateStream(...)`. Ends when the stream's enumerator is disposed |
| `vitreous publish {Notification}` | `Publish(...)` |
| `vitreous handle {Handler}` | each notification handler, as a child of the publish span |

| Tag | Value |
|---|---|
| `vitreous.message.type` | full name of the message type |
| `vitreous.message.kind` | `command`, `query`, `stream` or `notification` |
| `vitreous.dispatch.outcome` | `success`, `failure` (a failed `Result`), `exception` or `canceled` |
| `error.type` | `Error.Code` for a failed `Result`, the exception's full type name for an exception |
| `vitreous.handler.type` | full name of the handler type (handler spans only) |
| `vitreous.stream.items` | items the stream produced (stream spans only) |
| `vitreous.cache.hit` | `true` when `CachingBehavior` served the result from cache |

- A failed `Result` sets the span status to `Error`, with `Error.Message` as the description. The span records only `Error.Code`, never `Error.Extensions` or the message payload.
- An exception sets the status to `Error` and adds an `exception` event, then propagates unchanged. A cancellation requested by the caller's token is not an error.
- The handler runs with the dispatch span as `Activity.Current`, so the spans it starts (database or HTTP calls) are children of it. This also holds inside a stream's iterator.
- A stream span stays open until the stream is enumerated to the end, broken out of, faulted or canceled. **A stream that is never enumerated never ends its span.**

**Metrics:**

| Instrument | Meter | Tags |
|---|---|---|
| `vitreous.dispatch.duration` (histogram, `s`) | `Vitreous` | `vitreous.message.kind`, `vitreous.message.type`, `vitreous.dispatch.outcome`, `error.type` |
| `vitreous.cache.requests` (counter) | `Vitreous.Caching` | `vitreous.cache.result` (`hit`, `miss`, or `coalesced` when stampede protection shared another caller's handler run), `vitreous.message.type` |
| `vitreous.cache.invalidations` (counter) | `Vitreous.Caching` | `vitreous.cache.invalidation.kind` (`key` or `tag`), `vitreous.message.type` |

The DevUI and OpenTelemetry are independent: either works without the other, and both can run together.

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
- **`VitreousDispatcher.g.cs`** — an internal `ISender` / `IPublisher` implementation that dispatches with one lookup on the message's concrete type, with no reflection. A message type without its own handler falls back to the nearest base type that has one.

No attributes, no startup overhead. The generator runs during `dotnet build` and produces errors or warnings in your IDE when handler contracts are violated.

Handlers and messages may be classes, records, or record structs, and may be nested inside other public/internal types. Private nested handlers cannot be registered and are reported as `VIT001`.

#### Diagnostics

| ID | Severity | Trigger |
|---|---|---|
| `VIT001` | Warning | A command, query, or stream query has no registered handler |
| `VIT002` | Error | A command, query, or stream query has more than one handler |
| `VIT003` | Warning | An async iterator returning `IAsyncEnumerable` inside a stream query handler is missing `[EnumeratorCancellation]` on its `CancellationToken` parameter — callers using `.WithCancellation(ct)` will not be able to cancel mid-stream |

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

> **Tip:** install `Vitreous.SourceGeneration` on the entry-point project (the one that contains `Program.cs` and calls `AddVitreousHandlers()`). Handler projects don't need it. The generated types are `internal`, so a test project that also installs it gets its own registrations without conflicting.

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
builder.Services.AddVitreousDevUI(options =>
{
    options.PathPrefix             = "/vitreous"; // URL where the UI is mounted
    options.TraceBufferCapacity    = 500;         // number of traces kept in memory
    options.MaxCapturedStreamItems = 100;         // stream items serialized into the Output tab
    // options.Enabled = true;                    // default null: Development only
});

var app = builder.Build();

app.MapVitreousDevUI();   // or app.UseVitreousDevUI() to mount it as middleware
```

Navigate to `http://localhost:{port}/vitreous` (or your configured `PathPrefix`) to open the dashboard.

The UI is **off outside Development** unless you set `options.Enabled = true`. While it is off, no endpoints are mapped and the tracing behaviors pass straight through, so calling `AddVitreousDevUI()` unconditionally costs nothing in Production. When you do enable it beyond your machine, protect it:

```csharp
app.MapVitreousDevUI().RequireAuthorization("Admins");
```

#### Sensitive data

Captured input, output and stream items are masked as `"***"` for properties whose name contains one of `options.RedactedPropertyNames` (by default `password`, `secret`, `token`, `apiKey`, `authorization`, `creditCard`, `cardNumber`, `cvv`, `ssn`), and for anything marked `[Sensitive]`:

```csharp
public sealed record LoginCommand(string UserName, [Sensitive] string Pin) : ICommand<Guid>;
```

Masking applies to serialized messages and results; text your handlers write to `ILogger` is captured as-is.

### What it captures

Every command, query, notification, and stream query dispatch is traced automatically — no attributes or manual instrumentation required. Each trace includes:

| Field | Description |
|---|---|
| Message type | Simple and fully-qualified type name |
| Category | `command`, `query`, `notification`, or `streamquery` |
| Duration | Full wall-clock time including all pipeline behaviors |
| Outcome | `Success` or `Failure`, with error code and message on failure, plus exception type and stack trace when an exception is involved |
| Cache hit | Marked as `Cached` when `Vitreous.Caching` short-circuits the handler |
| Input | JSON-serialized request message (sensitive values masked) |
| Output | JSON-serialized success value (sensitive values masked) |
| Logs | All `ILogger` calls emitted inside the handler during the dispatch |
| Timestamp | Wall-clock time when the dispatch began |

Stream query traces include additional fields:

| Field | Description |
|---|---|
| Stream phase | `Opened` (stream is live and yielding items) or `Closed` (stream completed or failed) |
| Item count | Number of items yielded before the stream closed |
| Live duration | A ticking elapsed-time counter is shown in the UI while the stream is open |

### How it works

`AddVitreousDevUI()` inserts `TracingBehavior<,>` (for commands and queries), `NotificationTracingBehavior<>` (for notifications) and `StreamTracingBehavior<,>` (for stream queries) as the **outermost** pipeline behaviors regardless of what else is registered, so every dispatch — including cache hits from inner behaviors — is always captured. Log capture is implemented via a custom `ILoggerProvider` backed by an `AsyncLocal`, so log entries are attributed to the correct dispatch even under concurrent requests.

Stream dispatch emits two SSE events: `Opened` (when the stream starts yielding) and `Closed` (when it completes or errors). The dashboard shows a live ticking duration counter while the stream is open and freezes it on `Closed`.

The UI connects over Server-Sent Events (`/vitreous/events`). On page load it replays the full in-memory ring buffer, then streams new traces live. No WebSocket, no polling.

### Endpoints

| Endpoint | Description |
|---|---|
| `GET {prefix}` | The single-file dashboard UI |
| `GET {prefix}/events` | SSE stream (replay + live) |
| `GET {prefix}/api/traces` | JSON snapshot of the ring buffer |
| `GET {prefix}/api/config` | Settings the UI reads on load (trace buffer size) |

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

Keys are stored as `{KeyPrefix}:{CacheKey}` with exactly one `:` separator (a prefix that already ends in `:` is not doubled), identically for both backends.

Other options (the Redis backend has the same ones):

- `DefaultAbsoluteExpiration` — TTL for queries that set neither expiration. Default `null` (never expires).
- `EnableStampedeProtection` — concurrent misses for the same key share one handler call. Default `true`.
- `JsonSerializerOptions` — used by the default serializer. Register your own `IVitreousCacheSerializer` to replace it.

Values are stored together with their result type, so two queries that share a cache key but return different types get a miss instead of each other's data.

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

Internally, each tag is a Redis Set. A value and its tag memberships are written together in one Lua script, and each tag Set expires with the longest-lived entry it holds. `RemoveByTagAsync` detaches the Set atomically, then deletes its entries in pages with `SSCAN` and `UNLINK`, so a large tag never blocks Redis.

> **Redis Cluster:** an entry and its tags are written in one script, so they must share a hash slot. Use the same hash tag in both prefixes, e.g. `KeyPrefix = "{myapp}:"` and `TagPrefix = "{myapp}:tag:"`.

If Redis is unreachable, requests keep working: messages that don't use the cache never touch Redis, and cache reads and writes fail fast and fall back to the handler. The connection created from `ConnectionString` uses `abortConnect=false` and reconnects in the background.

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

### Mapping errors to HTTP status codes

`Error.Code` is a plain string, so a single extension method keeps endpoint code free of status-code logic:

```csharp
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) =>
        result.Match(
            value => onSuccess?.Invoke(value) ?? Results.Ok(value),
            error => Results.Problem(
                title:      error.Code,
                detail:     error.Message,
                statusCode: error.Code switch
                {
                    "NOT_FOUND"    => StatusCodes.Status404NotFound,
                    "VALIDATION"   => StatusCodes.Status400BadRequest,
                    "UNAUTHORIZED" => StatusCodes.Status401Unauthorized,
                    "CONFLICT"     => StatusCodes.Status409Conflict,
                    _              => StatusCodes.Status500InternalServerError
                },
                extensions: error.Extensions));
}

app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
    (await sender.Send(new GetOrderQuery(id), ct)).ToHttpResult());
```

---

## 5. Troubleshooting

**`No handler registered for command 'X'` at runtime**
The dispatcher could not find a handler. Check that:
- `Vitreous.SourceGeneration` is installed on the *entry-point* project (the one calling `AddVitreousHandlers()`), **or** you passed the handler assemblies explicitly: `AddVitreousHandlers(typeof(X).Assembly)`.
- The handler is a non-abstract, non-generic `public` or `internal` class.
- Rebuild the project — the generated code only updates on build.

**`AddVitreousHandlers() was called without any assemblies to scan`**
The source-generated overload is not present, so the call fell through to the reflection overload. Install `Vitreous.SourceGeneration` or pass assemblies.

**`VIT001: No handler is registered for 'X'`**
A command, query, or stream query has no handler in the compilation or any referenced project. Add one, or ignore the warning if the message is only dispatched by another application.

**`VIT002: Multiple handlers found for 'X'`**
Commands and queries must have exactly one handler. If you need fan-out, use a notification instead.

**`Cannot resolve scoped service 'ISender' from root provider`**
Handlers are scoped by default, so `ISender` and `IPublisher` are scoped too. Resolve them from a scope (e.g. `IServiceScopeFactory.CreateScope()` in a `BackgroundService`), or set `DefaultHandlerLifetime = ServiceLifetime.Singleton` if no handler has scoped dependencies.

**Nothing shows in the DevUI**
`UseVitreousDevUI()` must run in the environment you are testing (the samples gate it on `IsDevelopment()`), and `AddVitreousDevUI()` must be called before `builder.Build()`. Open `/vitreous` in a browser and trigger a request.

---

## License

MIT
