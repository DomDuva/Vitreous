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
/// A delegate representing the terminal step of a void-command pipeline.
/// The handler still returns the non-generic <see cref="Result"/>; the executor adapts it
/// to <c>Result&lt;Unit&gt;</c> before passing it through behaviors.
/// </summary>
/// <typeparam name="TCommand">The void command type.</typeparam>
public delegate ValueTask<Result> CommandHandlerDelegate<TCommand>(
    TCommand command, CancellationToken ct);

/// <summary>
/// A delegate representing the next step in a notification pipeline.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public delegate ValueTask NotificationHandlerDelegate<TNotification>(
    TNotification notification, CancellationToken ct);

/// <summary>
/// Cross-cutting behavior that wraps the dispatch of any message:
/// commands returning <typeparamref name="TResult"/>, queries, and void commands
/// (where <typeparamref name="TResult"/> is <see cref="Unit"/>).
/// </summary>
/// <typeparam name="TMessage">
/// The command or query type (<see cref="ICommand{TResult}"/>, <see cref="IQuery{TResult}"/>,
/// or <see cref="ICommand"/> for void commands, in which case <typeparamref name="TResult"/>
/// is <see cref="Unit"/>).
/// </typeparam>
/// <typeparam name="TResult">
/// The success value type, or <see cref="Unit"/> for void commands.
/// </typeparam>
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

/// <summary>
/// A delegate representing the next step in a stream query pipeline.
/// Returns a <see cref="Result{T}"/> wrapping the lazy <see cref="IAsyncEnumerable{TResult}"/>
/// so that setup failures short-circuit before any item is produced.
/// </summary>
/// <typeparam name="TMessage">The stream query type.</typeparam>
/// <typeparam name="TResult">The type of each item yielded by the stream.</typeparam>
public delegate ValueTask<Result<IAsyncEnumerable<TResult>>> StreamQueryHandlerDelegate<TMessage, TResult>(
    TMessage message, CancellationToken ct);

/// <summary>
/// Cross-cutting behavior that wraps the dispatch of a stream query.
/// Behaviors execute before the stream is opened and can short-circuit with a
/// <see cref="Result{T}"/> failure before any item is produced.
/// </summary>
/// <typeparam name="TMessage">
/// The stream query type — must implement <see cref="IStreamQuery{TResult}"/>.
/// </typeparam>
/// <typeparam name="TResult">The type of each item yielded by the stream.</typeparam>
public interface IStreamPipelineBehavior<TMessage, TResult>
    where TMessage : IStreamQuery<TResult>
{
    /// <summary>
    /// Executes this behavior step, calling <paramref name="next"/> to continue the pipeline.
    /// </summary>
    ValueTask<Result<IAsyncEnumerable<TResult>>> Handle(
        TMessage message,
        StreamQueryHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default);
}
