---
title: Source generator
description: What Vitreous.SourceGeneration emits, how it discovers handlers across projects, and the diagnostics it reports.
sidebar:
  order: 6
---

`Vitreous.SourceGeneration` is a build-time Roslyn incremental generator referenced as `PrivateAssets="all"` — it produces no runtime dependency. It scans your project's compilation and emits:

- **`VitreousServiceCollectionExtensions.g.cs`** — the `AddVitreousHandlers()` extension method that registers every discovered handler and replaces the reflection-based sender with the generated dispatcher.
- **`VitreousDispatcher.g.cs`** — an internal `ISender` / `IPublisher` implementation that dispatches with one lookup on the message's concrete type, with no reflection. A message type without its own handler falls back to the nearest base type that has one.

No attributes, no startup overhead. The generator runs during `dotnet build` and produces errors or warnings in your IDE when handler contracts are violated.

Handlers and messages may be classes, records, or record structs, and may be nested inside other public/internal types. Private nested handlers cannot be registered and are reported as `VIT001`.

The generator also finds the open-generic pipeline behaviors in your project and its references, and emits them closed over every message whose types satisfy their constraints, so they work under Native AOT. See [Native AOT and trimming](/Vitreous/guides/native-aot/#open-generic-behaviors).

## Diagnostics

| ID | Severity | Trigger |
|---|---|---|
| `VIT001` | Warning | A command, query, or stream query has no registered handler |
| `VIT002` | Error | A command, query, or stream query has more than one handler |
| `VIT003` | Warning | An async iterator returning `IAsyncEnumerable` inside a stream query handler is missing `[EnumeratorCancellation]` on its `CancellationToken` parameter — callers using `.WithCancellation(ct)` will not be able to cancel mid-stream |

Each one has a cause and a fix in the [diagnostics reference](/Vitreous/reference/diagnostics/).

## Multi-project solutions (DDD, Clean Architecture)

The generator automatically scans both the current project's source files **and** every referenced assembly that directly references `Vitreous`. Handlers can live in separate projects with no extra configuration.

```
MyApp.Api              ← install Vitreous.SourceGeneration here only
  ├── ref MyApp.Application    (commands, queries, handlers)
  └── ref MyApp.Infrastructure (DbContext, integration handlers)
```

Application and infrastructure projects — core package only:

```sh
dotnet add package Vitreous
```

Entry-point project only:

```sh
dotnet add package Vitreous
dotnet add package Vitreous.SourceGeneration
```

```csharp
// MyApp.Api / Program.cs — unchanged, handlers from all referenced projects are discovered
builder.Services.AddVitreousHandlers();
```

:::tip
Install `Vitreous.SourceGeneration` on the entry-point project (the one that contains `Program.cs` and calls `AddVitreousHandlers()`). Handler projects don't need it. The generated types are `internal`, so a test project that also installs it gets its own registrations without conflicting.
:::

The [Clean Architecture and DDD guide](/Vitreous/guides/clean-architecture-ddd/) walks through a three-project sample.

## Compiler compatibility

The generator is built against Roslyn 4.8, so it loads in every SDK from 8.0.100 on. CI builds a consumer app against the packed packages on SDK 8.0.100, 9.0.100, 10.0.100 and 11.0.100 to keep it that way.
