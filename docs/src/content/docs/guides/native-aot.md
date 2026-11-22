---
title: Native AOT and trimming
description: Publishing Vitreous apps with Native AOT, and what each package supports.
sidebar:
  label: Native AOT
  order: 4
  badge:
    text: 0.4.0+
    variant: note
---

`Vitreous`, `Vitreous.Caching` and `Vitreous.Caching.Redis` are trim- and AOT-compatible. Publish with `<PublishAot>true</PublishAot>` and register handlers with the generated `AddVitreousHandlers()`. It registers the generated dispatcher only, so nothing that dispatches with reflection is left in the app.

The reflection-based APIs, `AddVitreous()` and `AddVitreousHandlers(params Assembly[])`, are marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. Calling them in a trimmed or AOT app raises IL2026 and IL3050.

## Caching

Cached values are serialized with System.Text.Json, and reflection-based JSON is off in AOT apps. Give the caching options a `JsonSerializerContext` that lists every cached result type:

```csharp
builder.Services.AddVitreousCaching(o =>
    o.JsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = AppJsonContext.Default,
    });

[JsonSerializable(typeof(OrderDto))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
```

If a type is missing from the context, the cache read or write fails and is logged, and the handler runs as if the entry wasn't cached.

## Validation

`Vitreous.FluentValidation` is not marked AOT-compatible, because FluentValidation itself is not annotated for trimming.

## DevUI

`Vitreous.DevUI` serializes arbitrary messages with reflection, so it isn't AOT-compatible. `AddVitreousDevUI()`, `UseVitreousDevUI()` and `MapVitreousDevUI()` raise IL2026 and IL3050. Leave the package out of AOT builds.

## Open-generic behaviors

Under Native AOT, Microsoft.Extensions.DependencyInjection can't close an open-generic service over a value type. A void command (`Unit` is a struct), a result such as `int` or `Guid`, or a `record struct` message would then throw `InvalidOperationException: Unable to create a generic service for type ... because ... is a ValueType`. The source generator avoids this. It finds the open-generic behaviors in your project and its references, and emits them closed over every message whose types satisfy their constraints. `AddVitreousBehavior` registers those closed behaviors in place of the open generic, and `AddVitreousCaching()` and `AddVitreousRedisCaching()` use it. Two cases are still left to DI as open generics:

- A behavior registered directly as an open-generic service, for example with `services.AddSingleton(typeof(IPipelineBehavior<,>), ...)`.
- A behavior the generated code can't name or check. That covers a behavior that is private, or internal to another project. It also covers a constraint on a variant interface that the message implements with different type arguments, such as `where TResult : IEnumerable<object>` with an `IReadOnlyList<string>` result.

## The AOT sample

[`samples/Vitreous.Sample.Aot`](https://github.com/DomDuva/Vitreous/tree/main/samples/Vitreous.Sample.Aot) covers a command, a void command that invalidates the cache, a cached query, a `record struct` command with an `int` result, a notification with two handlers, a stream query, an open-generic behavior and a closed one. CI publishes it with Native AOT on .NET 10 and .NET 11 and calls every endpoint. On a Linux x64 VM it produces one 10.9 MB executable that answers its first request about 100 ms after starting, compared with about 450 ms for the same app on the JIT. MediatR can't be published with Native AOT, so there is no comparison.
