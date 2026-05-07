# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

