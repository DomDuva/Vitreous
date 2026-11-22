---
title: OpenTelemetry
description: Traces and metrics for every dispatch through System.Diagnostics, with no extra package.
sidebar:
  order: 3
  badge:
    text: 0.4.0+
    variant: note
---

Vitreous reports every dispatch through `System.Diagnostics`, so there is no extra package to install. Subscribe to its activity source and meters:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(VitreousTelemetry.ActivitySourceName))   // "Vitreous"
    .WithMetrics(m => m.AddMeter(VitreousTelemetry.MeterName,              // "Vitreous"
                                 VitreousCachingTelemetry.MeterName));     // "Vitreous.Caching"
```

Nothing is recorded until a listener subscribes. With no listener, a dispatch takes the same path as before and still allocates nothing. With a listener that records every span and measurement, a send costs about 0.7 µs and 576 B more (.NET 11, `TelemetryBenchmarks`), most of it the `Activity` itself.

## Spans

Spans are `ActivityKind.Internal`, a child of the current span, e.g. the ASP.NET Core request.

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

## Metrics

| Instrument | Meter | Tags |
|---|---|---|
| `vitreous.dispatch.duration` (histogram, `s`) | `Vitreous` | `vitreous.message.kind`, `vitreous.message.type`, `vitreous.dispatch.outcome`, `error.type` |
| `vitreous.cache.requests` (counter) | `Vitreous.Caching` | `vitreous.cache.result` (`hit`, `miss`, or `coalesced` when stampede protection shared another caller's handler run), `vitreous.message.type` |
| `vitreous.cache.invalidations` (counter) | `Vitreous.Caching` | `vitreous.cache.invalidation.kind` (`key` or `tag`), `vitreous.message.type` |

The [Developer UI](/Vitreous/guides/developer-ui/) and OpenTelemetry are independent: either works without the other, and both can run together.
