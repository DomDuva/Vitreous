namespace Vitreous.Abstractions;

/// <summary>
/// Handles a query that reads state and returns a value of type <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResult">The success value type.</typeparam>
public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>Handles the given <paramref name="query"/> and returns a result.</summary>
    ValueTask<Result<TResult>> Handle(TQuery query, CancellationToken ct = default);
}
