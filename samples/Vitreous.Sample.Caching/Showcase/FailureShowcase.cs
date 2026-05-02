using Vitreous.Abstractions;

namespace Vitreous.Sample.Caching.Showcase;

// ── Commands ──────────────────────────────────────────────────────────────────
// Each command takes no input and always returns a Result failure so that the
// Vitreous DevUI (and any connected API client) can observe every error shape.
// Plain ICommand<string> — no IInvalidatesCache — these are error-only demos.

public sealed record FailNotFoundCommand     : ICommand<string>;
public sealed record FailValidationCommand   : ICommand<string>;
public sealed record FailUnauthorizedCommand : ICommand<string>;
public sealed record FailConflictCommand     : ICommand<string>;
public sealed record FailInternalCommand     : ICommand<string>;
public sealed record FailCustomCommand       : ICommand<string>;

public sealed class FailNotFoundCommandHandler : ICommandHandler<FailNotFoundCommand, string>
{
    public ValueTask<Result<string>> Handle(FailNotFoundCommand command, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.NotFound("The requested resource could not be found. This command always fails to demonstrate a NOT_FOUND error.")));
}

public sealed class FailValidationCommandHandler : ICommandHandler<FailValidationCommand, string>
{
    public ValueTask<Result<string>> Handle(FailValidationCommand command, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.Validation(
                "One or more fields are invalid. This command always fails to demonstrate a VALIDATION error.",
                new Dictionary<string, string[]>
                {
                    ["Name"]     = ["Name is required.", "Name must be at least 2 characters."],
                    ["Quantity"] = ["Quantity must be greater than zero."]
                })));
}

public sealed class FailUnauthorizedCommandHandler : ICommandHandler<FailUnauthorizedCommand, string>
{
    public ValueTask<Result<string>> Handle(FailUnauthorizedCommand command, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.Unauthorized("You are not authorised to perform this operation. This command always fails to demonstrate an UNAUTHORIZED error.")));
}

public sealed class FailConflictCommandHandler : ICommandHandler<FailConflictCommand, string>
{
    public ValueTask<Result<string>> Handle(FailConflictCommand command, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.Conflict("A resource with the same identifier already exists. This command always fails to demonstrate a CONFLICT error.")));
}

public sealed class FailInternalCommandHandler : ICommandHandler<FailInternalCommand, string>
{
    public ValueTask<Result<string>> Handle(FailInternalCommand command, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.Internal(
                "An unexpected error occurred on the server. This command always fails to demonstrate an INTERNAL error.",
                new InvalidOperationException("Something went wrong deep in the call stack."))));
}

public sealed class FailCustomCommandHandler : ICommandHandler<FailCustomCommand, string>
{
    public ValueTask<Result<string>> Handle(FailCustomCommand command, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.Custom(
                "DEMO",
                "This is a custom application error. This command always fails to demonstrate a custom error with code DEMO.",
                new Dictionary<string, object?> { ["hint"] = "Custom errors carry any extra metadata you like." })));
}

// ── Queries ───────────────────────────────────────────────────────────────────
// Mirror of the commands above, but as read-side queries.
// Plain IQuery<string> — no ICacheableQuery — these are error-only demos.

public sealed record FailNotFoundQuery     : IQuery<string>;
public sealed record FailValidationQuery   : IQuery<string>;
public sealed record FailUnauthorizedQuery : IQuery<string>;
public sealed record FailConflictQuery     : IQuery<string>;
public sealed record FailInternalQuery     : IQuery<string>;
public sealed record FailCustomQuery       : IQuery<string>;

public sealed class FailNotFoundQueryHandler : IQueryHandler<FailNotFoundQuery, string>
{
    public ValueTask<Result<string>> Handle(FailNotFoundQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.NotFound("The requested resource could not be found. This query always fails to demonstrate a NOT_FOUND error.")));
}

public sealed class FailValidationQueryHandler : IQueryHandler<FailValidationQuery, string>
{
    public ValueTask<Result<string>> Handle(FailValidationQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.Validation(
                "One or more fields are invalid. This query always fails to demonstrate a VALIDATION error.",
                new Dictionary<string, string[]>
                {
                    ["Name"]     = ["Name is required.", "Name must be at least 2 characters."],
                    ["Quantity"] = ["Quantity must be greater than zero."]
                })));
}

public sealed class FailUnauthorizedQueryHandler : IQueryHandler<FailUnauthorizedQuery, string>
{
    public ValueTask<Result<string>> Handle(FailUnauthorizedQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.Unauthorized("You are not authorised to perform this operation. This query always fails to demonstrate an UNAUTHORIZED error.")));
}

public sealed class FailConflictQueryHandler : IQueryHandler<FailConflictQuery, string>
{
    public ValueTask<Result<string>> Handle(FailConflictQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.Conflict("A resource with the same identifier already exists. This query always fails to demonstrate a CONFLICT error.")));
}

public sealed class FailInternalQueryHandler : IQueryHandler<FailInternalQuery, string>
{
    public ValueTask<Result<string>> Handle(FailInternalQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.Internal(
                "An unexpected error occurred on the server. This query always fails to demonstrate an INTERNAL error.",
                new InvalidOperationException("Something went wrong deep in the call stack."))));
}

public sealed class FailCustomQueryHandler : IQueryHandler<FailCustomQuery, string>
{
    public ValueTask<Result<string>> Handle(FailCustomQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Result<string>.Fail(
            Error.Custom(
                "DEMO",
                "This is a custom application error. This query always fails to demonstrate a custom error with code DEMO.",
                new Dictionary<string, object?> { ["hint"] = "Custom errors carry any extra metadata you like." })));
}
