---
title: Installation
description: The Vitreous packages, which ones you need, and how to register them.
sidebar:
  order: 1
---

## Packages

| Package | Purpose |
|---|---|
| [`Vitreous`](https://www.nuget.org/packages/Vitreous) | Core mediator: commands, queries, notifications, pipeline behaviors, Result type |
| [`Vitreous.SourceGeneration`](https://www.nuget.org/packages/Vitreous.SourceGeneration) | Roslyn incremental generator — eliminates all runtime reflection |
| [`Vitreous.DevUI`](https://www.nuget.org/packages/Vitreous.DevUI) | Embedded developer dashboard: live traces, logs, cache hit indicators |
| [`Vitreous.Caching`](https://www.nuget.org/packages/Vitreous.Caching) | Pipeline behaviors for `IDistributedCache`-backed query caching |
| [`Vitreous.Caching.Redis`](https://www.nuget.org/packages/Vitreous.Caching.Redis) | Redis backend with tag-based bulk invalidation |
| [`Vitreous.FluentValidation`](https://www.nuget.org/packages/Vitreous.FluentValidation) | Pipeline behaviors that run FluentValidation validators before the handler |
| [`Vitreous.Templates`](https://www.nuget.org/packages/Vitreous.Templates) | `dotnet new` templates for a new project. See [Project templates](/Vitreous/guides/templates/) |

Every runtime package targets .NET 8, 9, 10 and 11.

### Which packages do you need

- **Always:** `Vitreous` in every project that declares messages or handlers.
- **Recommended:** `Vitreous.SourceGeneration` in the entry-point project, the one that contains `Program.cs` and calls `AddVitreousHandlers()`. Projects that only hold messages and handlers don't need it. See [Multi-project solutions](/Vitreous/concepts/source-generator/#multi-project-solutions-ddd-clean-architecture).
- **Optional:** `Vitreous.DevUI`, `Vitreous.Caching` or `Vitreous.Caching.Redis`, and `Vitreous.FluentValidation`, when you use those features.

## Install

Starting a new project? `dotnet new install Vitreous.Templates`, then `dotnet new vitreous-api -n MyApp`, creates one with everything below already set up. There are templates for a console app, a worker service and a layered Clean Architecture solution too. See [Project templates](/Vitreous/guides/templates/).

To add Vitreous to an existing project:

```sh
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

Calling `AddVitreousHandlers()` with no arguments and without the source generator installed throws at startup with a message explaining both options — it never silently registers nothing. See [Registration methods](/Vitreous/concepts/registration/) for how the two compare.

## Options

Both overloads accept an optional `Action<VitreousOptions>` callback:

```csharp
builder.Services.AddVitreousHandlers(options =>
{
    options.DefaultHandlerLifetime      = ServiceLifetime.Scoped;                // default
    options.NotificationPublishStrategy = NotificationPublishStrategy.Sequential; // default; set to Parallel for concurrent fan-out
});
```

`DefaultHandlerLifetime` applies to every discovered handler. The default, `Scoped`, is safe for handlers that depend on scoped services such as a `DbContext`. `ISender` and `IPublisher` are scoped unless the lifetime is `Singleton`, so resolve them from a scope: minimal APIs and controllers do this automatically, while background services should create one with `IServiceScopeFactory`. Use `Singleton` only when every handler is stateless and has no scoped dependencies.

All options are listed in the [options reference](/Vitreous/reference/options/).
