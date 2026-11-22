---
title: Options
description: Every option on VitreousOptions, VitreousCachingOptions, VitreousRedisCachingOptions and VitreousDevUIOptions, with defaults.
sidebar:
  order: 1
---

## VitreousOptions

Passed to `AddVitreousHandlers(options => ...)`, either overload. Package: `Vitreous`.

| Option | Type | Default | Description |
|---|---|---|---|
| `DefaultHandlerLifetime` | `ServiceLifetime` | `Scoped` | Lifetime of every registered handler. `ISender` and `IPublisher` are scoped too unless this is `Singleton`, so resolve them from a scope. Use `Singleton` only when every handler is stateless and has no scoped dependencies. |
| `NotificationPublishStrategy` | `NotificationPublishStrategy` | `Sequential` | `Sequential` runs notification handlers in order and stops at the first exception. `Parallel` runs them concurrently with `Task.WhenAll` and collects every exception into an `AggregateException`. |

## VitreousCachingOptions

Passed to `AddVitreousCaching(options => ...)`. Package: `Vitreous.Caching`.

| Option | Type | Default | Description |
|---|---|---|---|
| `KeyPrefix` | `string` | `"vitreous:"` | Prepended to every cache key, with exactly one `:` between the prefix and the key. |
| `DefaultAbsoluteExpiration` | `TimeSpan?` | `null` | Expiration for queries that set neither `AbsoluteExpiration` nor `SlidingExpiration`. `null` means such entries never expire. |
| `EnableStampedeProtection` | `bool` | `true` | Concurrent misses for the same key within the process share one handler run, and receive the same result instance. |
| `JsonSerializerOptions` | `JsonSerializerOptions` | camelCase | Used by the default System.Text.Json serializer. Set a `JsonSerializerContext` as its `TypeInfoResolver` for Native AOT. Ignored when you register your own `IVitreousCacheSerializer`. |

## VitreousRedisCachingOptions

Passed to `AddVitreousRedisCaching(options => ...)`. Package: `Vitreous.Caching.Redis`.

| Option | Type | Default | Description |
|---|---|---|---|
| `ConnectionString` | `string?` | `null` | A StackExchange.Redis configuration string, such as `"localhost:6379"`. Required unless an `IConnectionMultiplexer` is already registered. The connection uses `abortConnect=false`, fails fast while Redis is down and reconnects in the background. |
| `KeyPrefix` | `string` | `"vitreous:"` | Prepended to every cache key. |
| `TagPrefix` | `string` | `"vtag:"` | Prefix of the Redis Sets that track which keys belong to each tag. On Redis Cluster, use the same hash tag as `KeyPrefix`. |
| `DefaultAbsoluteExpiration` | `TimeSpan?` | `null` | As for `VitreousCachingOptions`. |
| `EnableStampedeProtection` | `bool` | `true` | As for `VitreousCachingOptions`. |
| `JsonSerializerOptions` | `JsonSerializerOptions` | camelCase | As for `VitreousCachingOptions`. |

## VitreousDevUIOptions

Passed to `AddVitreousDevUI(options => ...)`. Package: `Vitreous.DevUI`.

| Option | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool?` | `null` | `null` turns the UI and tracing on only in the `Development` environment. `true` turns them on everywhere (protect the UI with `MapVitreousDevUI().RequireAuthorization()`), `false` turns them off. While off, no endpoints are mapped and the tracing behaviors pass straight through. |
| `PathPrefix` | `string` | `"/vitreous"` | URL path the UI is mounted at. |
| `TraceBufferCapacity` | `int` | `500` | Traces kept in memory. The oldest is dropped when the buffer is full. |
| `MaxCapturedStreamItems` | `int` | `100` | Stream items serialized into a stream trace's Output tab. Items beyond this are counted but not stored. |
| `RedactedPropertyNames` | `ISet<string>` | `password`, `secret`, `token`, `apiKey`, `authorization`, `creditCard`, `cardNumber`, `cvv`, `ssn` | Property names whose values are masked as `"***"`. Matching is case-insensitive and by substring, so `token` also masks `accessToken`. Properties marked `[Sensitive]` are always masked. The set is read-only as a property, so add to it: `options.RedactedPropertyNames.Add("iban")`. |
| `SseHeartbeatInterval` | `TimeSpan` | 15 seconds | How often an idle live-trace connection gets a keep-alive comment, so proxies don't close it. |
