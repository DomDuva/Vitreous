namespace Vitreous.FluentValidation.Behaviors;

/// <summary>
/// Pipeline behavior that runs every registered <see cref="IValidator{T}"/> for a command or
/// query before its handler. When validation fails the handler is not called, and the dispatch
/// returns <see cref="Error.Validation"/> with the failures grouped by property name.
/// </summary>
/// <remarks>
/// Messages without a validator pass straight through. Void commands run through this behavior
/// too, with <typeparamref name="TResult"/> = <see cref="Unit"/>. Validators run one after
/// another, and every failure from every validator is reported.
/// </remarks>
public sealed class ValidationBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
{
    private readonly IValidator<TMessage>[] _validators;

    public ValidationBehavior(IEnumerable<IValidator<TMessage>> validators)
        => _validators = ValidationRunner.ToArray(validators);

    public ValueTask<Result<TResult>> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        // No validators: no async state machine, the next step runs directly.
        if (_validators.Length == 0)
            return next(message, ct);

        return ValidateThenHandle(message, next, ct);
    }

    private async ValueTask<Result<TResult>> ValidateThenHandle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct)
    {
        var error = await ValidationRunner.ValidateAsync(_validators, message, ct);
        if (error is not null)
            return Result<TResult>.Fail(error);

        return await next(message, ct);
    }
}
