# Vitreous

[![Vitreous](https://img.shields.io/nuget/v/Vitreous.svg?label=Vitreous)](https://www.nuget.org/packages/Vitreous)
[![Downloads](https://img.shields.io/nuget/dt/Vitreous.svg?label=downloads)](https://www.nuget.org/packages/Vitreous)

[![SourceGeneration](https://img.shields.io/nuget/v/Vitreous.SourceGeneration.svg?label=SourceGeneration)](https://www.nuget.org/packages/Vitreous.SourceGeneration)
[![DevUI](https://img.shields.io/nuget/v/Vitreous.DevUI.svg?label=DevUI)](https://www.nuget.org/packages/Vitreous.DevUI)
[![Caching](https://img.shields.io/nuget/v/Vitreous.Caching.svg?label=Caching)](https://www.nuget.org/packages/Vitreous.Caching)
[![Caching.Redis](https://img.shields.io/nuget/v/Vitreous.Caching.Redis.svg?label=Caching.Redis)](https://www.nuget.org/packages/Vitreous.Caching.Redis)
[![FluentValidation](https://img.shields.io/nuget/v/Vitreous.FluentValidation.svg?label=FluentValidation)](https://www.nuget.org/packages/Vitreous.FluentValidation)
[![Templates](https://img.shields.io/nuget/v/Vitreous.Templates.svg?label=Templates)](https://www.nuget.org/packages/Vitreous.Templates)
[![Docs](https://img.shields.io/badge/docs-domduva.github.io%2FVitreous-0e7490)](https://domduva.github.io/Vitreous/)

A high-performance, source-generated mediator for .NET — a modern alternative to MediatR with zero runtime reflection, first-class caching, and a built-in developer UI. Supports .NET 8, 9, 10 and 11.

**Documentation: [domduva.github.io/Vitreous](https://domduva.github.io/Vitreous/)** — guides, concepts, the API reference and a step-by-step tutorial.

## Packages

| Package | Purpose | Docs |
|---|---|---|
| `Vitreous` | Core mediator: commands, queries, notifications, pipeline behaviors, Result type | [Concepts](https://domduva.github.io/Vitreous/concepts/messages/) |
| `Vitreous.SourceGeneration` | Roslyn incremental generator — eliminates all runtime reflection | [Source generator](https://domduva.github.io/Vitreous/concepts/source-generator/) |
| `Vitreous.DevUI` | Embedded developer dashboard: live traces, logs, cache hit indicators | [Developer UI](https://domduva.github.io/Vitreous/guides/developer-ui/) |
| `Vitreous.Caching` | Pipeline behaviors for `IDistributedCache`-backed query caching | [Caching](https://domduva.github.io/Vitreous/guides/caching/) |
| `Vitreous.Caching.Redis` | Redis backend with tag-based bulk invalidation | [Caching](https://domduva.github.io/Vitreous/guides/caching/) |
| `Vitreous.FluentValidation` | Pipeline behaviors that run FluentValidation validators before the handler | [Validation](https://domduva.github.io/Vitreous/guides/validation/) |
| `Vitreous.Templates` | `dotnet new` templates for new projects, and for the messages and behaviors you add to them | [Project templates](https://domduva.github.io/Vitreous/guides/templates/) |

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

> **Starting a new project?** The [templates](#templates) create one with all of this already set up.

> **No source generator?** Call `AddVitreousHandlers(typeof(Program).Assembly)` instead and handlers are discovered by reflection at startup. See [Registration methods](https://domduva.github.io/Vitreous/concepts/registration/).

---

## Templates

```
dotnet new install Vitreous.Templates
dotnet new vitreous-api -n Shop
```

| Template | Creates |
|---|---|
| `vitreous-api` | A minimal API in vertical slices |
| `vitreous-console` | A command-line app in vertical slices |
| `vitreous-worker` | A background service in vertical slices |
| `vitreous-clean` | A layered solution (Domain, Application, Infrastructure) with a host: `--host api`, `console` or `worker` |
| `vitreous-command`, `vitreous-query`, `vitreous-notification`, `vitreous-behavior` | A message and its handler, or a pipeline behavior, in an existing project |

Every project template takes `-f net8.0` to `net11.0`, `--validation` and `--tests`. Most also take `--caching none|memory|redis`, and `vitreous-api` takes `--aot`. Start with vertical slices; pick `vitreous-clean` when you have business rules to keep apart from the database and the app around them. See [Project templates](https://domduva.github.io/Vitreous/guides/templates/) for every option and what is in each project.

---

## Performance

Dispatch against MediatR on .NET 11.0.0 (Intel Xeon Silver 4114, 2.2 GHz). Source: [`benchmark/Vitreous.Benchmarks`](https://github.com/DomDuva/Vitreous/tree/main/benchmark/Vitreous.Benchmarks).

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

Startup with 200+ handlers registered is **~150× faster** and allocates **~50× less** than MediatR, because `AddVitreousHandlers()` emits direct registration calls at build time. Full results and how to run them: [Performance](https://domduva.github.io/Vitreous/reference/performance/).

---

## Documentation

Everything lives on the [documentation site](https://domduva.github.io/Vitreous/).

**Getting started**
- [Installation](https://domduva.github.io/Vitreous/getting-started/installation/) — the packages, which ones you need, and how to register them
- [Quick start](https://domduva.github.io/Vitreous/getting-started/quick-start/) — three steps from an empty project to a working dispatch
- [Build your first feature](https://domduva.github.io/Vitreous/getting-started/first-feature/) — a tutorial: command, query, pipeline behavior and endpoints

**Concepts**
- [Messages](https://domduva.github.io/Vitreous/concepts/messages/) — commands, queries, stream queries and notifications
- [Handlers](https://domduva.github.io/Vitreous/concepts/handlers/)
- [Dispatching](https://domduva.github.io/Vitreous/concepts/dispatching/) — `ISender`, `IPublisher` and the stream query helpers
- [Results and errors](https://domduva.github.io/Vitreous/concepts/results-and-errors/) — `Result`, `Result<T>` and `Error`
- [Pipeline behaviors](https://domduva.github.io/Vitreous/concepts/pipeline-behaviors/)
- [Source generator](https://domduva.github.io/Vitreous/concepts/source-generator/) — generated code, multi-project solutions and diagnostics
- [Registration methods](https://domduva.github.io/Vitreous/concepts/registration/) — source-generated vs. reflection-based

**Guides**
- [Project templates](https://domduva.github.io/Vitreous/guides/templates/) for `dotnet new`
- [Validation](https://domduva.github.io/Vitreous/guides/validation/) with FluentValidation
- [Caching](https://domduva.github.io/Vitreous/guides/caching/) with `IDistributedCache` or Redis
- [OpenTelemetry](https://domduva.github.io/Vitreous/guides/opentelemetry/) traces and metrics
- [Native AOT and trimming](https://domduva.github.io/Vitreous/guides/native-aot/)
- [Developer UI](https://domduva.github.io/Vitreous/guides/developer-ui/)
- [Mapping errors to HTTP status codes](https://domduva.github.io/Vitreous/guides/http-error-mapping/)
- [Putting it all together](https://domduva.github.io/Vitreous/guides/putting-it-all-together/)
- [Clean Architecture and DDD](https://domduva.github.io/Vitreous/guides/clean-architecture-ddd/)
- [Migrating from MediatR](https://domduva.github.io/Vitreous/guides/migrating-from-mediatr/)

**Reference**
- [API reference](https://domduva.github.io/Vitreous/api/) — every public type, generated from the XML docs
- [Options](https://domduva.github.io/Vitreous/reference/options/) · [Diagnostics](https://domduva.github.io/Vitreous/reference/diagnostics/) · [Performance](https://domduva.github.io/Vitreous/reference/performance/)
- [Troubleshooting](https://domduva.github.io/Vitreous/troubleshooting/)
- [Changelog](https://github.com/DomDuva/Vitreous/blob/main/CHANGELOG.md)

---

## License

MIT
