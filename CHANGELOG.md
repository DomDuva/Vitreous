# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.4.0] - Unreleased

### Added

- OpenTelemetry tracing and metrics in `Vitreous`, through `System.Diagnostics` with no new dependency. Subscribe to `VitreousTelemetry.ActivitySourceName` and `VitreousTelemetry.MeterName` (both `"Vitreous"`). Every command, query, stream and notification gets a span, and each notification handler gets a child span. A failed `Result` sets the span status to `Error`, with `Error.Code` recorded as `error.type`. Durations are recorded in the `vitreous.dispatch.duration` histogram. Nothing is recorded, and nothing extra is allocated, until a listener subscribes.
- `Vitreous.Caching` counts cache hits, misses and coalesced loads (`vitreous.cache.requests`) and invalidations (`vitreous.cache.invalidations`) on the `VitreousCachingTelemetry.MeterName` meter (`"Vitreous.Caching"`).
- The source generator routes dispatch through the new telemetry when the referenced `Vitreous` has it. With an older `Vitreous` it emits the previous dispatcher, so upgrading only the generator still compiles.
- Native AOT and trimming support. `Vitreous`, `Vitreous.Caching` and `Vitreous.Caching.Redis` are marked `IsAotCompatible`. The generated `AddVitreousHandlers()` now calls the new `AddVitreousGenerated(options)`, which registers everything `AddVitreous(options)` does except the reflection-based sender. With an older `Vitreous` the generator still calls `AddVitreous(options)`.
- `samples/Vitreous.Sample.Aot`, a Native AOT web API. CI publishes it on .NET 10 and .NET 11 and checks every endpoint.

### Changed

- `AddVitreous()`, `AddVitreousHandlers(params Assembly[])`, `AddVitreousDevUI()`, `UseVitreousDevUI()` and `MapVitreousDevUI()` are marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. Their behavior is unchanged, but trimmed and AOT apps now get IL2026/IL3050 warnings for them.
- `SystemTextJsonVitreousCacheSerializer` serializes through the `JsonTypeInfo` from the configured options, so a `JsonSerializerContext` set as the options' `TypeInfoResolver` is used without reflection. Options without a resolver still fall back to reflection, as before.

## [0.3.0] - 2026-11-20

### Added

- `net11.0` target for `Vitreous`, `Vitreous.Caching`, `Vitreous.Caching.Redis` and `Vitreous.DevUI`.
- The public API of the runtime packages is tracked with `Microsoft.CodeAnalysis.PublicApiAnalyzers`.
- CI builds a consumer app against the packed `Vitreous` and `Vitreous.SourceGeneration` packages on SDK 8.0.100, 9.0.100, 10.0.100 and 11.0.100, so a generator the compiler would skip (CS9057) fails the build.

### Fixed

- The source generator keeps nullable annotations in handler type arguments. A handler for `IQuery<IReadOnlyList<string?>>` was registered as `IQueryHandler<Q, IReadOnlyList<string>>`, which raised CS8631 in nullable-enabled projects.

### Changed

- Benchmarks are run on .NET 10 and .NET 11.

## [0.2.0] - 2026-11-19

### Breaking

- **Handlers default to `Scoped`** (`Vitreous`) — `ISender`/`IPublisher` are singletons only when `DefaultHandlerLifetime` is `Singleton`. Previously a transient or singleton handler was resolved from the root provider, so a scoped `DbContext` was shared by every request. Resolve the sender from a scope, or opt into `Singleton`.
- **Generated types are `internal`** (`Vitreous.SourceGeneration`) — `VitreousDispatcher` and `VitreousGeneratedServiceCollectionExtensions`, so projects that both run the generator no longer conflict (CS0121/CS0436).
- **`Error.Internal` no longer copies the exception message into `Extensions`** (`Vitreous`) — the exception is kept on the new `[JsonIgnore] Error.Exception` property instead.
- **Cache entries use a typed envelope** (`Vitreous.Caching`) — entries written by 0.1.x read as misses.
- **The DevUI is inactive outside Development** (`Vitreous.DevUI`) unless `VitreousDevUIOptions.Enabled = true`; the tracing behavior constructors changed.

### Fixed

- Dispatch by exact message type: a derived message with its own handler no longer goes to its base type's handler.
- `ParallelPublisher` starts every handler even if one throws synchronously, and no longer throws `NullReferenceException` when handlers are only canceled.
- `TakeStream(n)` no longer pulls an extra item from the source; `CollectStream` propagates cancellation.
- `default(Result)`/`default(Result<T>)` report `Error.Uninitialized` instead of a null `Error`; `Fail(null)` throws.
- VIT003 only checks async iterators (it fired on helpers where the attribute causes CS8424); VIT002 is reported at each duplicate handler.
- A Redis outage no longer makes every dispatch fail: the connection is resolved lazily, does not abort on connect failure, and fails fast while disconnected.
- Redis tag Sets expire with their longest-lived entry instead of growing forever; tag removal no longer blocks Redis.
- The source generator loads on every SDK that can build `net8.0` (from 8.0.100). It was built against Roslyn 4.14, so the .NET 8 SDK and the .NET 9 SDK before 9.0.300 skipped it with warning CS9057, and `AddVitreousHandlers()` threw at startup.
- DevUI: faulted streams are shown as failed, slow live clients are dropped instead of buffering without limit, and the browser keeps at most `TraceBufferCapacity` traces.

### Added

- `MapVitreousDevUI()` (supports `RequireAuthorization()`), masking of sensitive values in captured data (`RedactedPropertyNames`, `[Sensitive]`), exception details in DevUI errors, SSE heartbeats, and a strict CSP on the DevUI page.
- `IVitreousCacheSerializer`, `DefaultAbsoluteExpiration` and stampede protection (`EnableStampedeProtection`) for caching.
- `Result`/`Result<T>` equality.

### Performance

- Incremental source generator: edits that cannot change the output no longer re-run discovery or regenerate code.
- Generated dispatch is a single lookup per message type, with no boxing or async state machine; sends without behaviors allocate nothing.

---

## [0.1.1] - 2026-10-16

### Fixed

- **Source generator never ran when installed from NuGet** (`Vitreous.SourceGeneration`) — the generator DLL was packed under `lib/netstandard2.0/` instead of `analyzers/dotnet/cs/`, so the compiler never loaded it and `AddVitreousHandlers()` silently resolved to the reflection overload with no assemblies, registering nothing. The package is now a proper analyzer package (`DevelopmentDependency`, no `lib/` output). CI verifies the layout on every build.
- **Generated registration dropped notification handlers** (`Vitreous.SourceGeneration`) — notification handlers were emitted with `TryAddSingleton`, which keeps only the first handler per notification type. They are now registered with `TryAddEnumerable`, matching the reflection path.
- **Generated registration ignored `DefaultHandlerLifetime`** (`Vitreous.SourceGeneration`) — handlers were always singletons. The configured lifetime is now honoured, and `ISender`/`IPublisher`/`VitreousDispatcher` become scoped when handlers are scoped so scoped dependencies (e.g. `DbContext`) resolve correctly.
- **Generated code required `ImplicitUsings`** (`Vitreous.SourceGeneration`) — projects with implicit usings disabled failed to compile the generated files. Explicit `using` directives are now emitted.
- **Generated code could collide on nested type names** (`Vitreous.SourceGeneration`) — two nested message types sharing a simple name (e.g. `CreateOrder.Command` / `DeleteOrder.Command`) produced duplicate pattern variables. Variables are now index-based.
- **`VIT001` never fired for `record` messages** (`Vitreous.SourceGeneration`) — only `class` declarations were inspected, yet the documented message style is `record`. Classes, records and (record) structs are now scanned; record handlers are also discovered. Private nested handlers (which cannot be referenced from generated code) are skipped and reported as `VIT001` instead of breaking the build. Nested types in referenced assemblies are now traversed.
- **Generated class name clashed with the core class** (`Vitreous.SourceGeneration`) — the emitted `partial class VitreousServiceCollectionExtensions` shares its fully-qualified name with the core type and triggers CS0436 whenever either is referenced by name. The generated class is now `VitreousGeneratedServiceCollectionExtensions`; the `AddVitreousHandlers()` call site is unchanged.
- **Reflection sender resolved behaviors by interface, not concrete type** (`Vitreous`) — `IPipelineBehavior<ICommand<T>, T>` was requested instead of `IPipelineBehavior<MyCommand, T>`, so behaviors closed over a specific message type never ran and open-generic behaviors saw `ICommand`1` as the message name (breaking DevUI labels). `ReflectionSender` now builds and caches a strongly-typed dispatch delegate per message type; reflection is used once per type, not per call.
- **`AddVitreousHandlers()` with no assemblies registered nothing** (`Vitreous`) — it now throws an `InvalidOperationException` explaining that either `Vitreous.SourceGeneration` must be installed or assemblies must be passed. Assembly scanning also skips open generic types and tolerates `ReflectionTypeLoadException`.
- **Notifications were never traced** (`Vitreous.DevUI`) — `NotificationTracingBehavior<>` existed but was not registered by `AddVitreousDevUI()`. It is now inserted as the outermost notification behavior.
- **Handler log order was scrambled** (`Vitreous.DevUI`) — `TraceContext` used a `ConcurrentBag`, which has no ordering guarantee. Logs are now kept in insertion order.
- **Stream traces captured every item in memory** (`Vitreous.DevUI`) — long or infinite streams grew without bound. Capture is now limited by the new `VitreousDevUIOptions.MaxCapturedStreamItems` (default 100); the item count is still exact and the output notes how many items were not captured. A stream that faults mid-iteration is now reported as a failed `Closed` trace with the exception logged.
- **Inconsistent cache key format** (`Vitreous.Caching`, `Vitreous.Caching.Redis`) — the default `"vitreous:"` prefix produced `vitreous::key` with the `IDistributedCache` backend and `vitreous:key` with Redis. Both now use `CacheKeys.Combine`, which guarantees exactly one separator.
- **Redis registration failed late with an opaque DI error** (`Vitreous.Caching.Redis`) — `AddVitreousRedisCaching()` now throws immediately with a clear message when neither a connection string nor an `IConnectionMultiplexer` is available.
- **Solution did not build** — the sample/test projects pulled in `Microsoft.OpenApi` 2.0.0, which has a known vulnerability (NU1903) that `TreatWarningsAsErrors` turned into a build error. `Microsoft.AspNetCore.OpenApi` and `Scalar.AspNetCore` were bumped to patched versions.

### Added

- **CI workflow** — builds, tests, packs, and verifies the analyzer package layout on every push and pull request.
- **README quick start and troubleshooting sections**, plus an HTTP error-mapping example.
- **`CacheKeys.Combine`** (`Vitreous.Caching`) — public helper for building prefixed keys consistently.
- Tests covering the reflection registration path, generator handling of records/structs/nested/private types, notification/lifetime registration semantics, generated-code compilation, DevUI traces (including notifications and streams), and cache key formatting.

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

