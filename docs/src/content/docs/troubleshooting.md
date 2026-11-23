---
title: Troubleshooting
description: Common errors and warnings, what causes them, and how to fix them.
---

## No handler registered for command 'X' at runtime

The dispatcher could not find a handler. Check that:

- `Vitreous.SourceGeneration` is installed on the *entry-point* project (the one calling `AddVitreousHandlers()`), **or** you passed the handler assemblies explicitly: `AddVitreousHandlers(typeof(X).Assembly)`.
- The handler is a non-abstract, non-generic `public` or `internal` class.
- Rebuild the project — the generated code only updates on build.

## AddVitreousHandlers() was called without any assemblies to scan

The source-generated overload is not present, so the call fell through to the reflection overload. Install `Vitreous.SourceGeneration` or pass assemblies.

## VIT001: No handler is registered for 'X'

A command, query, or stream query has no handler in the compilation or any referenced project. Add one, or ignore the warning if the message is only dispatched by another application. See [VIT001](/Vitreous/reference/diagnostics/#vit001).

## VIT002: Multiple handlers found for 'X'

Commands and queries must have exactly one handler. If you need fan-out, use a notification instead. See [VIT002](/Vitreous/reference/diagnostics/#vit002).

## Cannot resolve scoped service 'ISender' from root provider

Handlers are scoped by default, so `ISender` and `IPublisher` are scoped too. Resolve them from a scope (e.g. `IServiceScopeFactory.CreateScope()` in a `BackgroundService`), or set `DefaultHandlerLifetime = ServiceLifetime.Singleton` if no handler has scoped dependencies. The [`vitreous-worker`](/Vitreous/guides/templates/#vitreous-worker) and [`vitreous-console`](/Vitreous/guides/templates/#vitreous-console) templates show both cases as working code.

## IL2026 / IL3050 on AddVitreousHandlers(...) or AddVitreous()

The trim and AOT analyzers flag the reflection-based registration. Install `Vitreous.SourceGeneration` on the entry-point project and call `AddVitreousHandlers()` with no assemblies. See [Native AOT and trimming](/Vitreous/guides/native-aot/).

## Unable to create a generic service for type 'IPipelineBehavior...' because '...' is a ValueType

In a Native AOT app, an open-generic behavior registered directly as a service was closed over a value type: a void command, an `int`/`Guid` result or a struct message. Register it with `AddVitreousBehavior(typeof(MyBehavior<,>))` after `AddVitreousHandlers()`, with `Vitreous.SourceGeneration` installed. See [Open-generic behaviors](/Vitreous/guides/native-aot/#open-generic-behaviors).

## Nothing shows in the DevUI

`UseVitreousDevUI()` must run in the environment you are testing (the samples gate it on `IsDevelopment()`), and `AddVitreousDevUI()` must be called before `builder.Build()`. Open `/vitreous` in a browser and trigger a request. Outside Development the UI is off unless `options.Enabled = true`. See [Developer UI](/Vitreous/guides/developer-ui/).
