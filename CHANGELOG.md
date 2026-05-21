# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.1.0] - 2026-05-21

### Added

- **Parallel notification fan-out** (`Vitreous`) — new `NotificationPublishStrategy` enum on `VitreousOptions`. Set `NotificationPublishStrategy = NotificationPublishStrategy.Parallel` to dispatch all `INotificationHandler<T>` instances concurrently via `Task.WhenAll`. Every handler runs to completion regardless of individual failures; all exceptions are collected and re-thrown as an `AggregateException`. Defaults to `Sequential` (existing fail-fast, in-order behaviour) — no breaking change. The source-generated `VitreousDispatcher` respects the option at runtime.

### Fixed

- **Notification handler registration** (`Vitreous`) — `INotificationHandler<T>` implementations were registered with `TryAddSingleton`, which silently ignored every handler after the first for a given notification type. Changed to `TryAddEnumerable`, ensuring all handlers are registered and invoked on `Publish`.
- **Assembly scanner missing stream query handlers** (`Vitreous`) — `IStreamQueryHandler<,>` was absent from the handler interface check in `AddVitreousHandlers(params Assembly[])`, causing stream query handlers to be silently skipped during reflection-based discovery.
- **`DefaultHandlerLifetime` not honoured** (`Vitreous`) — the `DefaultHandlerLifetime` option configured in `AddVitreous()` was not propagated to `AddVitreousHandlers(params Assembly[])`, so handlers were always registered as singletons regardless of the configured lifetime.
- **`TracingBehavior` category detection** (`Vitreous.DevUI`) — void commands (`ICommand`) were not recognised as commands by the tracing behavior; only `ICommand<TResult>` was checked. Both interfaces are now detected and `HandlerType` resolves correctly for each.
- **`Result<T>.Ok()` null guard** (`Vitreous`) — `Result<T>.Ok(null!)` previously succeeded silently, producing a success `Result` carrying a null value. It now throws `ArgumentNullException` immediately.
- **`TraceAdded` event subscription leak** (`Vitreous.DevUI`) — the SSE broadcast delegate was appended to `TraceAdded` on every new SSE connection without first removing any prior subscription from the same client, causing duplicate events to be broadcast after reconnection. The handler is now unconditionally removed before being added.
- **`TracingBehavior` ambient context not cleared on exception** (`Vitreous.DevUI`) — the `AsyncLocal` trace context was only cleared in the success path; an unhandled handler exception left it set for the remainder of the request. Clearing is now performed in a `finally` block.
- **Cache key prefix separator missing** (`Vitreous.Caching`) — entries written with a non-empty `KeyPrefix` were stored under `{prefix}{key}` instead of `{prefix}:{key}`, causing keys with the same suffix to collide across different prefix namespaces. A colon separator is now always inserted between prefix and key.

---

## [0.1.0-beta.3] - 2026-05-12

### Added

- **Stream queries** (`IStreamQuery<TResult>`) — new message kind for lazy, pull-based sequences of items produced over time. Dispatched via `ISender.CreateStream<TResult>()`, which returns a `Result<IAsyncEnumerable<TResult>>` so that setup failures (validation, authorization) can be signalled before any item is produced.

- **`IStreamQueryHandler<TQuery, TResult>`** — handler interface for stream queries. The `Result` wrapper on the return type decouples setup from item production; once the stream is open, items flow as plain `TResult` values.

- **`StreamQueryHandler<TQuery, TResult>`** — optional abstract base class that eliminates the mandatory two-method split imposed by C#'s `yield return` / `ValueTask` restrictions. Override the single `Stream()` method and use `yield return` freely; the base class wraps the sequence in a successful `Result` automatically. Override `Handle()` to add validation before item production.

- **`IStreamPipelineBehavior<TMessage, TResult>`** and **`StreamQueryHandlerDelegate<TMessage, TResult>`** — stream-specific pipeline middleware. Behaviors run before the stream is opened and can short-circuit with a failure `Result` without producing any items. Register as open-generic services analogous to `IPipelineBehavior<,>`.

- **`StreamQueryExtensions`** (`Vitreous` core) — ergonomic helpers for `ISender` and `Result<IAsyncEnumerable<T>>`:
  - `CollectStream` — opens the stream and eagerly buffers all items into a `List<T>`; mid-stream exceptions are captured and returned as `Error.Internal`
  - `MapStream` — lazy per-item projection (equivalent to LINQ `Select`); no buffering
  - `FilterStream` — lazy predicate filtering (equivalent to LINQ `Where`); no buffering
  - `TakeStream` — lazy truncation after `n` items (equivalent to LINQ `Take`); no buffering
  - All helpers propagate a failure `Result` through untouched without opening the stream.

- **Source generator — `VIT003`** (`Vitreous.SourceGeneration`) — new warning emitted when a private `IAsyncEnumerable`-returning iterator method inside a stream query handler is missing `[EnumeratorCancellation]` on its `CancellationToken` parameter. Callers using `.WithCancellation(ct)` cannot cancel mid-stream without this attribute. `VIT001` (no handler) and `VIT002` (duplicate handler) now also cover stream query types in addition to commands and queries.

- **DevUI — live streaming traces** (`Vitreous.DevUI`) — `StreamTracingBehavior<,>` and `TracedAsyncEnumerable<T>` wrap every stream dispatch automatically. Two SSE events are emitted per dispatch: `Opened` (stream started) and `Closed` (stream completed or faulted). The dashboard merges these into a single row showing a pulsing **STREAMING** status badge with a live ticking duration counter while the stream is open; on close, the counter freezes and the final item count and serialized output are available in dedicated tabs.

### Fixed

- README NuGet badge labels were inconsistent across packages; corrected for clarity and uniformity.

---

## [0.1.0-beta.2] - 2026-05-06

### Changed

- **`PipelineExecutor`** — Added allocation-free fast paths for commands, queries, and notifications when no pipeline behaviors are registered. Skips `Reverse()` and delegate-chain construction entirely, reducing overhead in the zero-behavior case.
- **`PipelineExecutor`** — Void command dispatch (`ExecuteVoid`) now uses a non-async fast path when no behaviors are registered, eliminating `Unit` boxing and async state machine overhead.
- **`SequentialPublisher`** — Eliminates the `.ToArray()` re-allocation on `GetServices` results by using the DI container's backing array directly. Adds fast paths for the no-handler case (`ValueTask.CompletedTask` returned immediately, skipping behavior resolution), single-handler/no-behavior case (handler's `ValueTask` returned directly), and multi-handler/no-behavior case (skips delegate chain construction).

---

## [0.1.0-beta.1] - 2026-05-02

### Added

- **Core mediator** (`Vitreous`) — source-generated, zero-reflection command/query/notification dispatcher with a first-class `Result<T>` type. Supports .NET 8, .NET 9, and .NET 10.
- **Source generator** (`Vitreous.SourceGeneration`) — Roslyn incremental generator that emits `VitreousDispatcher` and `AddVitreousHandlers()` at build time. Fully AOT-compatible.
  - `VIT001` warning when a command or query type has no handler registered.
  - `VIT002` error when a command or query type has more than one handler (ambiguous dispatch).
- **Developer UI** (`Vitreous.DevUI`) — embedded real-time observability dashboard over Server-Sent Events. Captures timing, serialized input/output, cache-hit status, and handler logs per dispatch.
- **Caching** (`Vitreous.Caching`) — `IDistributedCache`-backed pipeline behaviors for query result caching and command-driven cache invalidation.
- **Redis caching** (`Vitreous.Caching.Redis`) — Redis backend with atomic, Lua-script-based tag invalidation for bulk cache eviction.

