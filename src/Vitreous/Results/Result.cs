using System.Diagnostics.CodeAnalysis;

namespace Vitreous;

/// <summary>
/// Represents the outcome of a command that does not return a value.
/// This is a discriminated union: either a success or an <see cref="Error"/>.
/// </summary>
public readonly struct Result : IEquatable<Result>
{
    private readonly Error? _error;

    private Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    // -------------------------------------------------------------------------
    // Singletons
    // -------------------------------------------------------------------------

    /// <summary>A pre-allocated successful result.</summary>
    public static readonly Result Success = new(true, null);

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    /// <summary>Returns <see langword="true"/> when the result represents a success.</summary>
    public bool IsSuccess { get; }

    /// <summary>Returns <see langword="true"/> when the result represents a failure.</summary>
    public bool IsFailure => !IsSuccess;

    // -------------------------------------------------------------------------
    // Value / error access
    // -------------------------------------------------------------------------

    /// <summary>
    /// The failure error. For an uninitialized (<see langword="default"/>) result this is
    /// <see cref="Vitreous.Error.Uninitialized"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a success.</exception>
    public Error Error => IsFailure
        ? FailureError
        : throw new InvalidOperationException("Cannot access Error on a successful Result.");

    // A default(Result) is a failure with no error; report it as Error.Uninitialized.
    private Error FailureError => _error ?? Error.Uninitialized;

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>Creates a failed result wrapping <paramref name="error"/>.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, error);
    }

    // -------------------------------------------------------------------------
    // Pattern matching
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes <paramref name="onSuccess"/> if successful,
    /// otherwise executes <paramref name="onFailure"/>.
    /// </summary>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(FailureError);

    /// <summary>Async variant of <see cref="Match{TOut}"/>.</summary>
    public ValueTask<TOut> MatchAsync<TOut>(
        Func<ValueTask<TOut>> onSuccess,
        Func<Error, ValueTask<TOut>> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(FailureError);

    // -------------------------------------------------------------------------
    // Implicit conversions
    // -------------------------------------------------------------------------

    /// <summary>Implicitly wraps an error as a failed result.</summary>
    public static implicit operator Result(Error error) => Fail(error);

    // -------------------------------------------------------------------------
    // Lift a non-generic Result into Result<T>
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a non-generic <see cref="Result"/> into <c>Result&lt;T&gt;</c>,
    /// mapping success to <paramref name="successValue"/>.
    /// </summary>
    public Result<T> ToResult<T>(T successValue) =>
        IsSuccess ? Result<T>.Ok(successValue) : Result<T>.Fail(FailureError);

    // -------------------------------------------------------------------------
    // Equality / display
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public bool Equals(Result other) =>
        IsSuccess == other.IsSuccess && (IsSuccess || FailureError.Equals(other.FailureError));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => IsSuccess ? 1 : FailureError.GetHashCode();

    /// <summary>Returns <see langword="true"/> when both results are equal.</summary>
    public static bool operator ==(Result left, Result right) => left.Equals(right);

    /// <summary>Returns <see langword="true"/> when the results differ.</summary>
    public static bool operator !=(Result left, Result right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() =>
        IsSuccess ? "Ok" : $"Fail({FailureError})";
}
