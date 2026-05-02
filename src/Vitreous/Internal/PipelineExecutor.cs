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
    /// Executes the pipeline for a void command.
    /// <para>
    /// Void-command behaviors are registered as <see cref="IPipelineBehavior{TCommand, Unit}"/>.
    /// The handler's <see cref="Result"/> is adapted to <see cref="Result{T}">Result&lt;Unit&gt;</see>
    /// before entering the behavior chain and converted back to <see cref="Result"/> on the way out,
    /// so behaviors see a uniform <c>Result&lt;TResult&gt;</c> regardless of whether the command
    /// returns a value.
    /// </para>
    /// </summary>
    public static async ValueTask<Result> ExecuteVoid<TCommand>(
        TCommand command,
        CommandHandlerDelegate<TCommand> terminal,
        IEnumerable<IPipelineBehavior<TCommand, Unit>> behaviors,
        CancellationToken ct)
        where TCommand : ICommand
    {
        // Adapt the void-handler terminal: Result → Result<Unit>
        MessageHandlerDelegate<TCommand, Unit> unitTerminal = async (cmd, token) =>
        {
            var r = await terminal(cmd, token);
            return r.IsSuccess
                ? Result<Unit>.Ok(Unit.Value)
                : Result<Unit>.Fail(r.Error);
        };

        // Build the chain with the same algorithm as Execute<,>
        MessageHandlerDelegate<TCommand, Unit> pipeline = unitTerminal;

        foreach (var behavior in behaviors.Reverse())
        {
            var next = pipeline;
            var current = behavior;
            pipeline = (cmd, token) => current.Handle(cmd, next, token);
        }

        // Run the chain, then adapt back: Result<Unit> → Result
        var unitResult = await pipeline(command, ct);
        return unitResult.IsSuccess ? Result.Success : Result.Fail(unitResult.Error);
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
