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
    public static Error Internal(string message, Exception? exception = null, string? code = null)
    {
        if (exception is null)
            return new(code ?? "INTERNAL", message);

        var extensions = new Dictionary<string, object?>
        {
            ["exceptionType"] = exception.GetType().Name,
            ["exceptionMessage"] = exception.Message
        };
        return new(code ?? "INTERNAL", message, extensions);
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
