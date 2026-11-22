---
title: Registration methods
description: The source-generated and reflection-based ways to register handlers, and how they compare.
sidebar:
  order: 7
---

Vitreous provides two ways to register handlers. They are **mutually exclusive** — choose one.

## Source-generated (recommended)

`AddVitreousHandlers()` has zero runtime reflection. The generator emits a concrete dispatcher at build time. Requires `Vitreous.SourceGeneration` on the entry-point project.

```csharp
builder.Services.AddVitreousHandlers();
```

## Reflection-based

`AddVitreousHandlers(params Assembly[])` scans the supplied assemblies at application startup. No source generator needed. Useful for plugin architectures or when compile-time generation is not available.

```csharp
builder.Services.AddVitreousHandlers(
    typeof(CreateOrderHandler).Assembly,
    typeof(SendConfirmationEmailHandler).Assembly
);
```

The reflection-based APIs are marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so trimmed and AOT apps get IL2026 and IL3050 for them. See [Native AOT and trimming](/Vitreous/guides/native-aot/).

## Comparison

| | `AddVitreousHandlers()` | `AddVitreousHandlers(assemblies)` |
|---|---|---|
| Discovery | Build time | Startup (reflection) |
| Runtime reflection | None | Yes |
| AOT compatible | Yes | No |
| Multi-project | Automatic | Manual — specify each assembly |
| Dispatcher | `VitreousDispatcher` (generated) | `ReflectionSender` |
| Best for | Most applications | Plugin architectures, dynamic loading |

Both overloads accept an `Action<VitreousOptions>`. See [Options](/Vitreous/reference/options/#vitreousoptions).
