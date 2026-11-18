using System.Diagnostics.CodeAnalysis;

namespace Vitreous;

/// <summary>
/// Represents the outcome of a command or query that returns a value of type <typeparamref name="T"/>.
/// This is a discriminated union: either a success value or an <see cref="Error"/>.
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
public readonly struct Result<T> : IEquatable<Result<T>>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
        _error = null;
    }

    private Result(Error error)
    {
        IsSuccess = false;
        _value = default;
        _error = error;
    }

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
    /// The success value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a failure.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot access Value on a failed Result. Error: {FailureError}");

    /// <summary>
    /// The failure error. For an uninitialized (<see langword="default"/>) result this is
    /// <see cref="Vitreous.Error.Uninitialized"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a success.</exception>
    public Error Error => IsFailure
        ? FailureError
        : throw new InvalidOperationException(
            "Cannot access Error on a successful Result.");

    // A default(Result<T>) is a failure with no error; report it as Error.Uninitialized.
    private Error FailureError => _error ?? Error.Uninitialized;

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>Creates a successful result wrapping <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is <see langword="null"/>.
    /// A successful Result cannot hold a null value.
    /// </exception>
    public static Result<T> Ok(T value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value),
                "A successful Result cannot hold a null value.");
        return new(value);
    }

    /// <summary>Creates a failed result wrapping <paramref name="error"/>.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result<T> Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }

    // -------------------------------------------------------------------------
    // Pattern matching / transformation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes <paramref name="onSuccess"/> if the result is successful,
    /// otherwise executes <paramref name="onFailure"/>.
    /// </summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(FailureError);

    /// <summary>Async variant of <see cref="Match{TOut}(Func{T,TOut},Func{Error,TOut})"/>.</summary>
    public ValueTask<TOut> MatchAsync<TOut>(
        Func<T, ValueTask<TOut>> onSuccess,
        Func<Error, ValueTask<TOut>> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(FailureError);

    /// <summary>
    /// Projects the success value using <paramref name="map"/>.
    /// Returns the original failure unchanged.
    /// </summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? Result<TOut>.Ok(map(_value!)) : Result<TOut>.Fail(FailureError);

    /// <summary>Async variant of <see cref="Map{TOut}(Func{T,TOut})"/>.</summary>
    public async ValueTask<Result<TOut>> MapAsync<TOut>(Func<T, ValueTask<TOut>> map) =>
        IsSuccess ? Result<TOut>.Ok(await map(_value!)) : Result<TOut>.Fail(FailureError);

    /// <summary>
    /// Chains a fallible projection using <paramref name="bind"/>.
    /// Returns the original failure unchanged if already failed.
    /// </summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind) =>
        IsSuccess ? bind(_value!) : Result<TOut>.Fail(FailureError);

    /// <summary>Async variant of <see cref="Bind{TOut}(Func{T,Result{TOut}})"/>.</summary>
    public ValueTask<Result<TOut>> BindAsync<TOut>(Func<T, ValueTask<Result<TOut>>> bind) =>
        IsSuccess ? bind(_value!) : ValueTask.FromResult(Result<TOut>.Fail(FailureError));

    // -------------------------------------------------------------------------
    // Implicit conversions — ergonomic construction
    // -------------------------------------------------------------------------

    /// <summary>Implicitly wraps a value as a successful result.</summary>
    public static implicit operator Result<T>(T value) => Ok(value);

    /// <summary>Implicitly wraps an error as a failed result.</summary>
    public static implicit operator Result<T>(Error error) => Fail(error);

    // -------------------------------------------------------------------------
    // Equality / display
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public bool Equals(Result<T> other) =>
        IsSuccess == other.IsSuccess &&
        (IsSuccess
            ? EqualityComparer<T>.Default.Equals(_value!, other._value!)
            : FailureError.Equals(other.FailureError));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Result<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        IsSuccess ? HashCode.Combine(true, _value) : FailureError.GetHashCode();

    /// <summary>Returns <see langword="true"/> when both results are equal.</summary>
    public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);

    /// <summary>Returns <see langword="true"/> when the results differ.</summary>
    public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() =>
        IsSuccess ? $"Ok({_value})" : $"Fail({FailureError})";
}
