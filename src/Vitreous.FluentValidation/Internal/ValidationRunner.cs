namespace Vitreous.FluentValidation.Internal;

/// <summary>
/// Runs a message's validators and turns their failures into <see cref="Error.Validation"/>.
/// Shared by <see cref="Behaviors.ValidationBehavior{TMessage, TResult}"/> and
/// <see cref="Behaviors.StreamValidationBehavior{TMessage, TResult}"/>.
/// </summary>
internal static class ValidationRunner
{
    internal const string FailureMessage = "Validation failed.";

    // Microsoft.Extensions.DependencyInjection already resolves IEnumerable<T> as an array.
    internal static IValidator<T>[] ToArray<T>(IEnumerable<IValidator<T>> validators) =>
        validators as IValidator<T>[] ?? validators.ToArray();

    /// <summary>
    /// Runs every validator and returns the combined failures as an <see cref="Error"/>,
    /// or <see langword="null"/> when the message is valid.
    /// </summary>
    internal static async ValueTask<Error?> ValidateAsync<T>(
        IValidator<T>[] validators,
        T message,
        CancellationToken ct)
    {
        List<ValidationFailure>? failures = null;

        // One at a time, not Task.WhenAll: validators often share a scoped service such as a
        // DbContext, which does not support concurrent calls.
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(message, ct);
            if (!result.IsValid)
                (failures ??= []).AddRange(result.Errors);
        }

        return failures is null ? null : ToError(failures);
    }

    /// <summary>
    /// Groups failures by property name, in the order the validators reported them. The result
    /// is the <c>failures</c> extension of <see cref="Error.Validation"/>, which ASP.NET Core's
    /// <c>Results.ValidationProblem</c> takes as is.
    /// </summary>
    private static Error ToError(List<ValidationFailure> failures)
    {
        var grouped = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var property in failures.GroupBy(f => f.PropertyName ?? string.Empty, StringComparer.Ordinal))
            grouped[property.Key] = property.Select(f => f.ErrorMessage).ToArray();

        return Error.Validation(FailureMessage, grouped);
    }
}
