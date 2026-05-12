namespace Vitreous.Abstractions;

/// <summary>
/// Optional base class for stream query handlers that eliminates the mandatory two-method
/// split imposed by C#'s restriction on mixing <c>yield return</c> with
/// <c>ValueTask&lt;Result&lt;IAsyncEnumerable&lt;T&gt;&gt;&gt;</c> returns.
/// </summary>
/// <typeparam name="TQuery">The stream query type.</typeparam>
/// <typeparam name="TResult">The type of each item yielded by the stream.</typeparam>
/// <remarks>
/// <para>
/// <b>Simple case — no validation:</b> override only <see cref="Stream"/> and use
/// <c>yield return</c> freely.
/// </para>
/// <code>
/// public sealed class StreamOrdersHandler : StreamQueryHandler&lt;StreamOrdersQuery, OrderDto&gt;
/// {
///     protected override async IAsyncEnumerable&lt;OrderDto&gt; Stream(
///         StreamOrdersQuery query, [EnumeratorCancellation] CancellationToken ct)
///     {
///         await foreach (var order in _db.StreamAsync(ct))
///             yield return Map(order);
///     }
/// }
/// </code>
/// <para>
/// <b>With validation:</b> override <see cref="Handle"/> and call
/// <see cref="Stream"/> manually after checking your preconditions.
/// </para>
/// <code>
/// public override ValueTask&lt;Result&lt;IAsyncEnumerable&lt;OrderDto&gt;&gt;&gt; Handle(
///     StreamOrdersQuery query, CancellationToken ct)
/// {
///     if (!IsAuthorized()) return new(Error.Unauthorized("Access denied."));
///     return new(Stream(query, ct));
/// }
/// </code>
/// </remarks>
public abstract class StreamQueryHandler<TQuery, TResult> : IStreamQueryHandler<TQuery, TResult>
    where TQuery : IStreamQuery<TResult>
{
    /// <summary>
    /// Produces the stream of items. Implement this with <c>yield return</c>.
    /// </summary>
    /// <remarks>
    /// Always decorate the <c>ct</c> parameter with <c>[EnumeratorCancellation]</c>
    /// so that callers using <c>.WithCancellation(ct)</c> can cancel mid-stream.
    /// </remarks>
    protected abstract IAsyncEnumerable<TResult> Stream(TQuery query, CancellationToken ct);

    /// <summary>
    /// Opens the stream, returning the lazy sequence on success or an <see cref="Error"/> on failure.
    /// Override this to add validation or authorization before items are produced.
    /// The default implementation returns <see cref="Stream"/> wrapped in a successful
    /// <see cref="Result{T}"/> with no additional setup.
    /// </summary>
    public virtual ValueTask<Result<IAsyncEnumerable<TResult>>> Handle(
        TQuery query,
        CancellationToken ct = default)
        => new(Result<IAsyncEnumerable<TResult>>.Ok(Stream(query, ct)));
}
