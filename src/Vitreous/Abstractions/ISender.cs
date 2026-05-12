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

    /// <summary>
    /// Opens a stream query and returns a lazy sequence of <typeparamref name="TResult"/> items.
    /// The <see cref="Result{T}"/> wrapper allows setup failures (validation, authorization) to be
    /// signalled before any item is produced. Once the stream is open, items flow as plain
    /// <typeparamref name="TResult"/>.
    /// </summary>
    ValueTask<Result<IAsyncEnumerable<TResult>>> CreateStream<TResult>(
        IStreamQuery<TResult> query,
        CancellationToken ct = default);
}
