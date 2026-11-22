---
title: Diagnostics
description: The build-time diagnostics VIT001, VIT002 and VIT003 reported by the source generator, with the cause and the fix for each.
sidebar:
  order: 2
---

`Vitreous.SourceGeneration` reports these diagnostics while your project builds, and in your IDE as you type. They use the category `Vitreous`.

| ID | Severity | Title |
|---|---|---|
| [`VIT001`](#vit001) | Warning | No Vitreous handler found |
| [`VIT002`](#vit002) | Error | Multiple Vitreous handlers found |
| [`VIT003`](#vit003) | Warning | Missing `[EnumeratorCancellation]` on stream handler |

## VIT001

**No handler is registered for '`X`'.** Implement `I…Handler<X, …>` in any project that references Vitreous.

**Cause.** A command, query or stream query has no handler in the compilation or in any referenced project that references `Vitreous`. It is also reported for a handler that is a private nested type, which the generated code can't reference.

**Fix.** Add a handler, in the same project or in any project the entry-point project references. If the handler exists but is nested, make it `internal` or `public`. If the message is only dispatched by another application, suppress the warning. When the message is declared in the project that runs the generator, `#pragma warning disable VIT001` around its declaration suppresses it for that message alone. A message from a referenced project is reported without a source location, so suppress it with `<NoWarn>$(NoWarn);VIT001</NoWarn>` in the entry-point project, which turns it off for every message.

Sending a message that has no handler fails at runtime with `No handler registered for command 'X'`. See [Troubleshooting](/Vitreous/troubleshooting/).

## VIT002

**Multiple handlers found for '`X`'**: `HandlerA`, `HandlerB`. Dispatch is ambiguous — remove all but one handler.

**Cause.** A command, query or stream query has more than one handler. Each must have exactly one, so it is a build error.

**Fix.** Remove all but one handler. If several pieces of code need to react to the same thing, publish a [notification](/Vitreous/concepts/messages/#notifications) instead: notifications can have any number of handlers.

## VIT003

**Async iterator '`Stream`' on '`Handler`' returns `IAsyncEnumerable<T>` and has a `CancellationToken` parameter, but it is not decorated with `[EnumeratorCancellation]`.**

**Cause.** An async iterator in a stream query handler takes a `CancellationToken` without the `[EnumeratorCancellation]` attribute. A caller that cancels with `.WithCancellation(ct)` then can't stop the stream mid-way, because the token never reaches the iterator.

**Fix.** Add the attribute from `System.Runtime.CompilerServices`:

```csharp
using System.Runtime.CompilerServices;

protected override async IAsyncEnumerable<OrderDto> Stream(
    GetOrderTicksQuery query,
    [EnumeratorCancellation] CancellationToken ct)
```

See [Stream query handlers](/Vitreous/concepts/handlers/#stream-query-handlers).
