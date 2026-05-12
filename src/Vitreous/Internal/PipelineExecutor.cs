using System.ComponentModel;
using Vitreous.Abstractions;

namespace Vitreous.Internal;

/// <summary>
/// Builds and executes a pipeline of <see cref="IPipelineBehavior{TMessage,TResult}"/> instances
/// around a terminal handler delegate.
/// </summary>
/// <remarks>
/// This type is <see langword="public"/> solely because the source-generated
/// <c>VitreousDispatcher</c> calls it directly. It is not intended for direct
/// use by application code.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
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
        // Fast path: no behaviors — skip Reverse() allocation entirely.
        // GetServices returns T[] from MSDI; Array.Empty<T>() is also ICollection<T>.
        if (behaviors is ICollection<IPipelineBehavior<TMessage, TResult>> { Count: 0 })
            return terminal(message, ct);

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
    /// When behaviors are present, the handler's <see cref="Result"/> is adapted to
    /// <see cref="Result{T}">Result&lt;Unit&gt;</see> before entering the behavior chain
    /// and converted back on the way out, so behaviors see a uniform
    /// <c>Result&lt;TResult&gt;</c> regardless of whether the command returns a value.
    /// When no behaviors are registered the terminal is called directly with no
    /// conversion overhead.
    /// </para>
    /// </summary>
    public static ValueTask<Result> ExecuteVoid<TCommand>(
        TCommand command,
        CommandHandlerDelegate<TCommand> terminal,
        IEnumerable<IPipelineBehavior<TCommand, Unit>> behaviors,
        CancellationToken ct)
        where TCommand : ICommand
    {
        // Fast path: no behaviors — call terminal directly, no Unit round-trip, no async
        // state machines.
        if (behaviors is ICollection<IPipelineBehavior<TCommand, Unit>> { Count: 0 })
            return terminal(command, ct);

        return ExecuteVoidCore(command, terminal, behaviors, ct);
    }

    private static async ValueTask<Result> ExecuteVoidCore<TCommand>(
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
    /// Executes the pipeline for a stream query.
    /// Behaviors are applied outermost-first (first registered = outermost wrapper).
    /// The terminal opens the stream and returns a <see cref="Result{T}"/> wrapping the lazy
    /// <see cref="IAsyncEnumerable{TResult}"/>; behaviors can short-circuit with a failure before
    /// any item is produced.
    /// </summary>
    public static ValueTask<Result<IAsyncEnumerable<TResult>>> ExecuteStream<TMessage, TResult>(
        TMessage message,
        StreamQueryHandlerDelegate<TMessage, TResult> terminal,
        IEnumerable<IStreamPipelineBehavior<TMessage, TResult>> behaviors,
        CancellationToken ct)
        where TMessage : IStreamQuery<TResult>
    {
        // Fast path: no behaviors — skip Reverse() allocation entirely.
        if (behaviors is ICollection<IStreamPipelineBehavior<TMessage, TResult>> { Count: 0 })
            return terminal(message, ct);

        // Build the pipeline chain right-to-left so the first behavior is outermost.
        StreamQueryHandlerDelegate<TMessage, TResult> pipeline = terminal;

        foreach (var behavior in behaviors.Reverse())
        {
            var next    = pipeline;
            var current = behavior;
            pipeline = (msg, token) => current.Handle(msg, next, token);
        }

        return pipeline(message, ct);
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
        // Fast path: no behaviors — skip Reverse() allocation entirely.
        if (behaviors is ICollection<INotificationPipelineBehavior<TNotification>> { Count: 0 })
            return terminal(notification, ct);

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
