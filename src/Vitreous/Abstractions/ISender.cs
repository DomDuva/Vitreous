namespace Vitreous.Abstractions;

/// <summary>
/// Dispatches commands and queries to their registered handlers,
/// executing any registered pipeline behaviors along the way.
/// </summary>
public interface ISender
{
    /// <summary>
    /// Sends a command that returns a value of type <typeparamref name="TResult"/>.
    /// </summary>
    ValueTask<Result<TResult>> Send<TResult>(
        ICommand<TResult> command,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a command that does not return a meaningful value.
    /// </summary>
    ValueTask<Result> Send(ICommand command, CancellationToken ct = default);

    /// <summary>
    /// Sends a query that returns a value of type <typeparamref name="TResult"/>.
    /// </summary>
    ValueTask<Result<TResult>> Send<TResult>(
        IQuery<TResult> query,
        CancellationToken ct = default);
}
