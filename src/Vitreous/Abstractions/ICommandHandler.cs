namespace Vitreous.Abstractions;

/// <summary>
/// Handles a command that returns a value of type <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The success value type.</typeparam>
public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>Handles the given <paramref name="command"/> and returns a result.</summary>
    ValueTask<Result<TResult>> Handle(TCommand command, CancellationToken ct = default);
}

/// <summary>
/// Handles a command that does not return a meaningful value.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface ICommandHandler<TCommand>
    where TCommand : ICommand
{
    /// <summary>Handles the given <paramref name="command"/> and returns a result.</summary>
    ValueTask<Result> Handle(TCommand command, CancellationToken ct = default);
}
