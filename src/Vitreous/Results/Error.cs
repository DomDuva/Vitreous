using System.Text.Json.Serialization;

namespace Vitreous;

/// <summary>
/// Represents a strongly-typed error produced by a command or query handler.
/// </summary>
/// <param name="Code">A short machine-readable error code (e.g. "NOT_FOUND", "VALIDATION").</param>
/// <param name="Message">A human-readable description of the error.</param>
/// <param name="Extensions">Optional metadata — field violations, correlation IDs, etc.</param>
public sealed record Error(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    /// <summary>
    /// The exception that caused this error, when one was supplied to <see cref="Internal"/>.
    /// Kept for logging and diagnostics only: it is never serialized, so exception details
    /// cannot leak to API clients through a serialized <see cref="Error"/>.
    /// </summary>
    [JsonIgnore]
    public Exception? Exception { get; init; }

    /// <summary>
    /// The error reported by an uninitialized (<see langword="default"/>) <see cref="Result"/>
    /// or <see cref="Result{T}"/>, which is a failure that was never given an error.
    /// </summary>
    public static Error Uninitialized { get; } = new(
        "UNINITIALIZED",
        "The Result was not initialized. Create results with Ok/Fail or the implicit conversions instead of default.");

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>Creates a "not found" error.</summary>
    public static Error NotFound(string message, string? code = null) =>
        new(code ?? "NOT_FOUND", message);

    /// <summary>Creates a validation error, optionally with per-field failures.</summary>
    public static Error Validation(
        string message,
        IReadOnlyDictionary<string, string[]>? failures = null,
        string? code = null)
    {
        if (failures is null || failures.Count == 0)
            return new(code ?? "VALIDATION", message);

        var extensions = new Dictionary<string, object?> { ["failures"] = failures };
        return new(code ?? "VALIDATION", message, extensions);
    }

    /// <summary>Creates an "unauthorized" error.</summary>
    public static Error Unauthorized(string message, string? code = null) =>
        new(code ?? "UNAUTHORIZED", message);

    /// <summary>Creates a "conflict" error (e.g. duplicate resource).</summary>
    public static Error Conflict(string message, string? code = null) =>
        new(code ?? "CONFLICT", message);

    /// <summary>Creates an "internal" error, optionally wrapping an exception.</summary>
    /// <remarks>
    /// The exception is available on <see cref="Exception"/> (with its stack trace) but is not
    /// serialized. Only its type name is added to <see cref="Extensions"/> as
    /// <c>exceptionType</c>; the exception message is deliberately left out because it often
    /// contains internal details that should not reach clients.
    /// </remarks>
    public static Error Internal(string message, Exception? exception = null, string? code = null)
    {
        if (exception is null)
            return new(code ?? "INTERNAL", message);

        var extensions = new Dictionary<string, object?>
        {
            ["exceptionType"] = exception.GetType().Name
        };
        return new(code ?? "INTERNAL", message, extensions) { Exception = exception };
    }

    /// <summary>Creates a custom error with any code, message, and optional extensions.</summary>
    public static Error Custom(
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(code, message, extensions);

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override string ToString() => $"[{Code}] {Message}";
}
