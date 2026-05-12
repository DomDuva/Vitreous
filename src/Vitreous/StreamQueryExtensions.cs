using System.Runtime.CompilerServices;
using Vitreous.Abstractions;

namespace Vitreous;

/// <summary>
/// Extension methods on <see cref="ISender"/> and <see cref="Result{T}"/>
/// that make working with stream queries more ergonomic.
/// </summary>
public static class StreamQueryExtensions
{
    // -------------------------------------------------------------------------
    // ISender helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens the stream query and eagerly collects all items into a <see cref="List{T}"/>,
    /// returning the collected list wrapped in a <see cref="Result{T}"/>.
    /// </summary>
    /// <remarks>
    /// Handles both setup failures (returned by the handler before any item is produced)
    /// and mid-stream exceptions (wrapped in an <see cref="Error.Internal"/> failure).
    /// Useful for tests and one-shot scenarios where the full result is needed at once.
    /// </remarks>
    public static async ValueTask<Result<List<TResult>>> CollectStream<TResult>(
        this ISender sender,
        IStreamQuery<TResult> query,
        CancellationToken ct = default)
    {
        var streamResult = await sender.CreateStream(query, ct);
        if (streamResult.IsFailure)
            return Result<List<TResult>>.Fail(streamResult.Error);

        try
        {
            var list = new List<TResult>();
            await foreach (var item in streamResult.Value.WithCancellation(ct))
                list.Add(item);
            return list;
        }
        catch (Exception ex)
        {
            return Error.Internal("Stream collection failed.", ex);
        }
    }

    // -------------------------------------------------------------------------
    // Result<IAsyncEnumerable<T>> functional helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Projects each item in the stream using <paramref name="mapper"/>.
    /// If the result is a failure, the failure passes through untouched.
    /// Equivalent to LINQ <c>Select</c> — lazy, no buffering.
    /// </summary>
    public static Result<IAsyncEnumerable<TOut>> MapStream<TIn, TOut>(
        this Result<IAsyncEnumerable<TIn>> result,
        Func<TIn, TOut> mapper)
        => result.Map(stream => SelectAsync(stream, mapper));

    /// <summary>
    /// Keeps only items in the stream that satisfy <paramref name="predicate"/>.
    /// If the result is a failure, the failure passes through untouched.
    /// Equivalent to LINQ <c>Where</c> — lazy, no buffering.
    /// </summary>
    public static Result<IAsyncEnumerable<T>> FilterStream<T>(
        this Result<IAsyncEnumerable<T>> result,
        Func<T, bool> predicate)
        => result.Map(stream => WhereAsync(stream, predicate));

    /// <summary>
    /// Stops the stream after at most <paramref name="count"/> items.
    /// If the result is a failure, the failure passes through untouched.
    /// Equivalent to LINQ <c>Take</c> — lazy, no buffering.
    /// </summary>
    public static Result<IAsyncEnumerable<T>> TakeStream<T>(
        this Result<IAsyncEnumerable<T>> result,
        int count)
        => result.Map(stream => TakeAsync(stream, count));

    // -------------------------------------------------------------------------
    // Private async iterators (no external package dependency)
    // -------------------------------------------------------------------------

    private static async IAsyncEnumerable<TOut> SelectAsync<TIn, TOut>(
        IAsyncEnumerable<TIn> source,
        Func<TIn, TOut> mapper,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in source.WithCancellation(ct))
            yield return mapper(item);
    }

    private static async IAsyncEnumerable<T> WhereAsync<T>(
        IAsyncEnumerable<T> source,
        Func<T, bool> predicate,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in source.WithCancellation(ct))
            if (predicate(item)) yield return item;
    }

    private static async IAsyncEnumerable<T> TakeAsync<T>(
        IAsyncEnumerable<T> source,
        int count,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var remaining = count;
        await foreach (var item in source.WithCancellation(ct))
        {
            if (remaining-- <= 0) yield break;
            yield return item;
        }
    }
}
