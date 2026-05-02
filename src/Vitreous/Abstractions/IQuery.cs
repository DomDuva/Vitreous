namespace Vitreous.Abstractions;

/// <summary>
/// Marker interface for a query that reads state and returns a value of type <typeparamref name="TResult"/>.
/// Queries should be side-effect free and idempotent.
/// Handled by a single <see cref="IQueryHandler{TQuery,TResult}"/>.
/// </summary>
/// <typeparam name="TResult">The type of the value returned on success.</typeparam>
public interface IQuery<TResult> { }
