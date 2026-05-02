using Vitreous.Abstractions;

namespace Vitreous.Internal;

/// <summary>
/// Builds and executes a pipeline of <see cref="IPipelineBehavior{TMessage,TResult}"/> instances
/// around a terminal handler delegate.
/// </summary>
public static class PipelineExecutor
{
    /// <summary>
    /// Executes the pipeline for a command/query returning <typeparamref name="TResult"/>.
    /// Behaviors are applied outermost-first (first registered = outermost wrapper).
    /// </summary>
    public static ValueTask<Result<TResult>> Execute<TMessage, TResult>(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> terminal,
        IEnumerable<IPipelineBehavior<TMessage, TResult>> behaviors,
        CancellationToken ct)
    {
        // Build the pipeline chain right-to-left so the first behavior is outermost.
        MessageHandlerDelegate<TMessage, TResult> pipeline = terminal;

        foreach (var behavior in behaviors.Reverse())
        {
            var next = pipeline;       // capture for closure
            var current = behavior;    // capture for closure
            pipeline = (msg, token) => current.Handle(msg, next, token);
        }

        return pipeline(message, ct);
    }

    /// <summary>
    /// Executes the pipeline for a void command returning <see cref="Result"/>.
    /// </summary>
    public static ValueTask<Result> ExecuteVoid<TCommand>(
        TCommand command,
        CommandHandlerDelegate<TCommand> terminal,
        IEnumerable<ICommandPipelineBehavior<TCommand>> behaviors,
        CancellationToken ct)
        where TCommand : ICommand
    {
        CommandHandlerDelegate<TCommand> pipeline = terminal;

        foreach (var behavior in behaviors.Reverse())
        {
            var next = pipeline;
            var current = behavior;
            pipeline = (cmd, token) => current.Handle(cmd, next, token);
        }

        return pipeline(command, ct);
    }

    /// <summary>
    /// Executes the notification pipeline.
    /// </summary>
    public static ValueTask ExecuteNotification<TNotification>(
        TNotification notification,
        NotificationHandlerDelegate<TNotification> terminal,
        IEnumerable<INotificationPipelineBehavior<TNotification>> behaviors,
        CancellationToken ct)
        where TNotification : INotification
    {
        NotificationHandlerDelegate<TNotification> pipeline = terminal;

        foreach (var behavior in behaviors.Reverse())
        {
            var next = pipeline;
            var current = behavior;
            pipeline = (n, token) => current.Handle(n, next, token);
        }

        return pipeline(notification, ct);
    }
}
