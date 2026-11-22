---
title: Results and errors
description: The Result and Result<T> types every handler returns, and the Error type with its factory methods.
sidebar:
  order: 4
---

## Result type

Every handler returns `Result` or `Result<T>`. Results are either a success value or an `Error` — never both.

```csharp
// Implicit conversions (the most common path)
Result<Guid> ok  = Guid.NewGuid();                     // T → Result<T>
Result<Guid> err = Error.NotFound("Order not found."); // Error → Result<T>

// Branch on outcome
string message = result.Match(
    id  => $"Created order {id}",
    err => $"Error: {err.Message}"
);

// Transform the success value
Result<string> idString = result.Map(id => id.ToString());

// Chain a fallible operation
Result<OrderDto> dto = result.Bind(id => LoadOrder(id));

// Async variants
string message = await result.MatchAsync(
    async id  => await FormatOrderAsync(id),
    async err => err.Message
);
```

`Unit` is a built-in struct that represents the absence of a value. It is used as `TResult` in behaviors when a void command is dispatched — you will not use it in handlers directly, but may encounter it in generic behavior constraints. See [Void commands and Unit](/Vitreous/concepts/pipeline-behaviors/#void-commands-and-unit).

## Error type

`Error` is a sealed record carrying a `Code` and `Message`, with optional extension data.

```csharp
Error.NotFound("Order 123 not found.")
Error.Validation("Invalid request.", failures: new Dictionary<string, string[]>
{
    ["customerId"] = ["Customer ID is required."],
    ["items"]      = ["At least one item is required."]
})
Error.Unauthorized("You do not have access to this resource.")
Error.Conflict("An order for this customer is already pending.")
Error.Internal("Unexpected failure.", exception: ex)
Error.Custom("PAYMENT_DECLINED", "Payment was declined by the processor.")
```

All factory methods accept an optional `code` override to replace the default code string.

`Error.Code` is a plain string, so one extension method can turn errors into HTTP responses. See [Mapping errors to HTTP status codes](/Vitreous/guides/http-error-mapping/).
