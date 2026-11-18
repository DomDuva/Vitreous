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
        // GetServices returns T[] from MSDI; anything else is materialized once.
        var list = AsList(behaviors);
        if (list.Count == 0)
            return terminal(message, ct);

        // Build the chain right-to-left so the first behavior is outermost. The outermost
        // behavior is called directly, so N behaviors cost N-1 wrappers. Each wrapper is a
        // fixed "next" for one position, so a behavior may call next more than once (retries).
        var next = terminal;
        for (var i = list.Count - 1; i > 0; i--)
            next = Wrap(list[i], next);

        return list[0].Handle(message, next, ct);
    }

    private static MessageHandlerDelegate<TMessage, TResult> Wrap<TMessage, TResult>(
        IPipelineBehavior<TMessage, TResult> behavior,
        MessageHandlerDelegate<TMessage, TResult> next) =>
        (message, ct) => behavior.Handle(message, next, ct);

    private static IReadOnlyList<T> AsList<T>(IEnumerable<T> items) =>
        items as IReadOnlyList<T> ?? items.ToArray();

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

        // Run the chain (same algorithm as Execute<,>), then adapt back: Result<Unit> → Result
        var unitResult = await Execute(command, unitTerminal, behaviors, ct);
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
        var list = AsList(behaviors);
        if (list.Count == 0)
            return terminal(message, ct);

        // Same chain as Execute<,>: outermost behavior called directly, one wrapper per inner one.
        var next = terminal;
        for (var i = list.Count - 1; i > 0; i--)
            next = WrapStream(list[i], next);

        return list[0].Handle(message, next, ct);
    }

    private static StreamQueryHandlerDelegate<TMessage, TResult> WrapStream<TMessage, TResult>(
        IStreamPipelineBehavior<TMessage, TResult> behavior,
        StreamQueryHandlerDelegate<TMessage, TResult> next)
        where TMessage : IStreamQuery<TResult> =>
        (message, ct) => behavior.Handle(message, next, ct);

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
        var list = AsList(behaviors);
        if (list.Count == 0)
            return terminal(notification, ct);

        // Same chain as Execute<,>: outermost behavior called directly, one wrapper per inner one.
        var next = terminal;
        for (var i = list.Count - 1; i > 0; i--)
            next = WrapNotification(list[i], next);

        return list[0].Handle(notification, next, ct);
    }

    private static NotificationHandlerDelegate<TNotification> WrapNotification<TNotification>(
        INotificationPipelineBehavior<TNotification> behavior,
        NotificationHandlerDelegate<TNotification> next)
        where TNotification : INotification =>
        (notification, ct) => behavior.Handle(notification, next, ct);
}
