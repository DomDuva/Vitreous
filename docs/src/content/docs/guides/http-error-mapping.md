---
title: Mapping errors to HTTP status codes
description: One extension method that turns a Result into an HTTP response, with the status code chosen from Error.Code.
sidebar:
  label: HTTP error mapping
  order: 6
---

`Error.Code` is a plain string, so a single extension method keeps endpoint code free of status-code logic:

```csharp
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) =>
        result.Match(
            value => onSuccess?.Invoke(value) ?? Results.Ok(value),
            error => Results.Problem(
                title:      error.Code,
                detail:     error.Message,
                statusCode: error.Code switch
                {
                    "NOT_FOUND"    => StatusCodes.Status404NotFound,
                    "VALIDATION"   => StatusCodes.Status400BadRequest,
                    "UNAUTHORIZED" => StatusCodes.Status401Unauthorized,
                    "CONFLICT"     => StatusCodes.Status409Conflict,
                    _              => StatusCodes.Status500InternalServerError
                },
                extensions: error.Extensions));
}

app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
    (await sender.Send(new GetOrderQuery(id), ct)).ToHttpResult());
```

The codes come from the `Error` factories. See [Error type](/Vitreous/concepts/results-and-errors/#error-type). For validation failures with per-property messages, see [Validation](/Vitreous/guides/validation/).
