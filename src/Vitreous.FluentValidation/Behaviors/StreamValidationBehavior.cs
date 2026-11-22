namespace Vitreous.FluentValidation.Behaviors;

/// <summary>
/// Stream pipeline behavior that runs every registered <see cref="IValidator{T}"/> for a stream
/// query before the stream is opened. When validation fails the handler is not called, and
/// <c>CreateStream</c> returns <see cref="Error.Validation"/> with the failures grouped by
/// property name.
/// </summary>
/// <remarks>
/// Queries without a validator pass straight through. Validation runs once, when the stream is
/// created, not for each item.
/// </remarks>
public sealed class StreamValidationBehavior<TMessage, TResult> : IStreamPipelineBehavior<TMessage, TResult>
    where TMessage : IStreamQuery<TResult>
{
    private readonly IValidator<TMessage>[] _validators;

    public StreamValidationBehavior(IEnumerable<IValidator<TMessage>> validators)
        => _validators = ValidationRunner.ToArray(validators);

    public ValueTask<Result<IAsyncEnumerable<TResult>>> Handle(
        TMessage message,
        StreamQueryHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        // No validators: no async state machine, the next step runs directly.
        if (_validators.Length == 0)
            return next(message, ct);

        return ValidateThenOpen(message, next, ct);
    }

    private async ValueTask<Result<IAsyncEnumerable<TResult>>> ValidateThenOpen(
        TMessage message,
        StreamQueryHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct)
    {
        var error = await ValidationRunner.ValidateAsync(_validators, message, ct);
        if (error is not null)
            return Result<IAsyncEnumerable<TResult>>.Fail(error);

        return await next(message, ct);
    }
}
