namespace Vitreous.Abstractions;

/// <summary>
/// Marker interface for a query that reads state and produces a stream of values of type
/// <typeparamref name="TResult"/> lazily over time.
/// Handled by a single <see cref="IStreamQueryHandler{TQuery,TResult}"/>.
/// </summary>
/// <typeparam name="TResult">The type of each item yielded by the stream.</typeparam>
public interface IStreamQuery<TResult> { }
