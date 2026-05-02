namespace Vitreous.Abstractions;

/// <summary>
/// A delegate representing the next step in a command/query pipeline.
/// The message and cancellation token are passed explicitly to avoid closure allocations.
/// </summary>
/// <typeparam name="TMessage">The command or query type.</typeparam>
/// <typeparam name="TResult">The success value type.</typeparam>
public delegate ValueTask<Result<TResult>> MessageHandlerDelegate<TMessage, TResult>(
    TMessage message, CancellationToken ct);

/// <summary>
/// A delegate representing the next step in a command pipeline for void commands.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public delegate ValueTask<Result> CommandHandlerDelegate<TCommand>(
    TCommand command, CancellationToken ct);

/// <summary>
/// A delegate representing the next step in a notification pipeline.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public delegate ValueTask NotificationHandlerDelegate<TNotification>(
    TNotification notification, CancellationToken ct);

/// <summary>
/// Cross-cutting behavior that wraps the dispatch of a command or query returning <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TMessage">The command or query type (<see cref="ICommand{TResult}"/> or <see cref="IQuery{TResult}"/>).</typeparam>
/// <typeparam name="TResult">The success value type.</typeparam>
public interface IPipelineBehavior<TMessage, TResult>
{
    /// <summary>
    /// Executes this behavior step, calling <paramref name="next"/> to continue the pipeline.
    /// </summary>
    ValueTask<Result<TResult>> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default);
}

/// <summary>
/// Cross-cutting behavior that wraps the dispatch of a void command.
/// </summary>
/// <typeparam name="TCommand">The command type (<see cref="ICommand"/>).</typeparam>
public interface ICommandPipelineBehavior<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    /// Executes this behavior step, calling <paramref name="next"/> to continue the pipeline.
    /// </summary>
    ValueTask<Result> Handle(
        TCommand command,
        CommandHandlerDelegate<TCommand> next,
        CancellationToken ct = default);
}

/// <summary>
/// Cross-cutting behavior that wraps the dispatch of a notification.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationPipelineBehavior<TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Executes this behavior step, calling <paramref name="next"/> to continue the pipeline.
    /// </summary>
    ValueTask Handle(
        TNotification notification,
        NotificationHandlerDelegate<TNotification> next,
        CancellationToken ct = default);
}
