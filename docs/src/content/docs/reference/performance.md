---
title: Performance
description: Dispatch and startup benchmarks against MediatR, and how to run them yourself.
sidebar:
  order: 3
---

Benchmarks run on .NET 11.0.0, Intel Xeon Silver 4114 (2.2 GHz). Source: [`benchmark/Vitreous.Benchmarks`](https://github.com/DomDuva/Vitreous/tree/main/benchmark/Vitreous.Benchmarks).

MediatR is benchmarked at its default Transient handler lifetime; Vitreous with `DefaultHandlerLifetime = Singleton`. Switching MediatR to Singleton makes no meaningful difference to dispatch latency or allocation.

## Dispatch (hot path)

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

## Startup (BuildServiceProvider)

The benchmark project registers 200+ handlers per library.

| | MediatR | Vitreous |
|---|---|---|
| Time | 10,061,341 ns | 67,193 ns |
| Allocated | 2,893,031 B | 58,416 B |

Vitreous is **~150× faster** to start and allocates **~50× less** — because `AddVitreousHandlers()` emits direct registration calls at build time with no assembly scanning or reflection.

## Telemetry overhead

With no OpenTelemetry listener attached, a dispatch allocates nothing extra. With a listener that records every span and measurement, a send costs about 0.7 µs and 576 B more (`TelemetryBenchmarks`). See [OpenTelemetry](/Vitreous/guides/opentelemetry/).

## Run the benchmarks

[`benchmark/run.sh`](https://github.com/DomDuva/Vitreous/blob/main/benchmark/run.sh) runs a build-time comparison of two equivalent minimal projects, then the BenchmarkDotNet runtime benchmarks:

```sh
./benchmark/run.sh                         # build-time comparison + all runtime benchmarks
./benchmark/run.sh --filter "*Dispatch*"   # build-time comparison + dispatch only
./benchmark/run.sh --filter "*Startup*"    # build-time comparison + startup only
```

Results are written to `benchmark/Vitreous.Benchmarks/BenchmarkDotNet.Artifacts/`.
