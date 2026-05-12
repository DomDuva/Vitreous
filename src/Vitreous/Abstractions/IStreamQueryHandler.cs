namespace Vitreous.Abstractions;

/// <summary>
/// Handles a stream query that reads state and produces items of type
/// <typeparamref name="TResult"/> lazily over time.
/// </summary>
/// <typeparam name="TQuery">The stream query type.</typeparam>
/// <typeparam name="TResult">The type of each item yielded by the stream.</typeparam>
/// <remarks>
/// The handler returns a <see cref="Result{T}"/> wrapping the <see cref="IAsyncEnumerable{T}"/>
/// so that setup failures (validation, authorization) can be signalled before any item is yielded.
/// Once the stream is open, items flow as plain <typeparamref name="TResult"/>.
/// <para>
/// Because C# does not allow <c>yield return</c> in a method that also returns
/// <c>ValueTask&lt;…&gt;</c>, split the handler into two methods: <c>Handle</c> for setup/validation
/// and a private iterator method for item production.
/// For handlers with no validation, use the optional <see cref="StreamQueryHandler{TQuery,TResult}"/>
/// base class instead — it removes this split entirely.
/// </para>
/// </remarks>
public interface IStreamQueryHandler<TQuery, TResult>
    where TQuery : IStreamQuery<TResult>
{
    /// <summary>
    /// Validates and opens the stream. On success, returns the lazy sequence of items.
    /// On failure, returns an <see cref="Error"/> before any item is produced.
    /// </summary>
    ValueTask<Result<IAsyncEnumerable<TResult>>> Handle(TQuery query, CancellationToken ct = default);
}
