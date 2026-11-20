using System.Diagnostics;

namespace Vitreous.Internal;

/// <summary>
/// Keeps a stream's dispatch span open while the stream is enumerated, counts its items and
/// ends the span when the enumerator is disposed: after the last item, an early
/// <see langword="break"/>, a fault or cancellation.
/// </summary>
/// <remarks>
/// Only the first enumeration belongs to the span. Enumerating the same stream again passes
/// straight through to the handler's stream.
/// </remarks>
internal sealed class TelemetryAsyncEnumerable<T> : IAsyncEnumerable<T>
{
    private readonly IAsyncEnumerable<T> _inner;
    private readonly DispatchTelemetry.DispatchScope _scope;
    private readonly CancellationToken _dispatchToken;
    private int _enumerated;

    internal TelemetryAsyncEnumerable(
        IAsyncEnumerable<T> inner,
        DispatchTelemetry.DispatchScope scope,
        CancellationToken dispatchToken)
    {
        _inner         = inner;
        _scope         = scope;
        _dispatchToken = dispatchToken;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _enumerated, 1) != 0)
            return _inner.GetAsyncEnumerator(cancellationToken);

        IAsyncEnumerator<T> inner;
        try
        {
            inner = _inner.GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            _scope.Fail(ex, IsCancellation(ex, _dispatchToken, cancellationToken));
            throw;
        }

        return new Enumerator(inner, _scope, _dispatchToken, cancellationToken);
    }

    private static bool IsCancellation(Exception ex, CancellationToken dispatchToken, CancellationToken enumeratorToken) =>
        DispatchTelemetry.IsCancellation(ex, dispatchToken) || DispatchTelemetry.IsCancellation(ex, enumeratorToken);

    private sealed class Enumerator : IAsyncEnumerator<T>
    {
        private readonly IAsyncEnumerator<T> _inner;
        private readonly DispatchTelemetry.DispatchScope _scope;
        private readonly CancellationToken _dispatchToken;
        private readonly CancellationToken _enumeratorToken;
        private long _items;
        private Exception? _fault;
        private int _disposed;

        internal Enumerator(
            IAsyncEnumerator<T> inner,
            DispatchTelemetry.DispatchScope scope,
            CancellationToken dispatchToken,
            CancellationToken enumeratorToken)
        {
            _inner           = inner;
            _scope           = scope;
            _dispatchToken   = dispatchToken;
            _enumeratorToken = enumeratorToken;
        }

        public T Current => _inner.Current;

        public async ValueTask<bool> MoveNextAsync()
        {
            // The handler's iterator body runs inside MoveNextAsync: make the stream span current
            // so that spans it starts (database calls, HTTP calls) are children of it. This is an
            // async method, so the change does not leak to the caller.
            if (_scope.Activity is { } activity)
                Activity.Current = activity;

            try
            {
                var hasNext = await _inner.MoveNextAsync();
                if (hasNext)
                    _items++;
                return hasNext;
            }
            catch (Exception ex)
            {
                _fault ??= ex;
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                await _inner.DisposeAsync();
            }
            catch (Exception ex)
            {
                _fault ??= ex;
                throw;
            }
            finally
            {
                if (_scope.Activity is { IsAllDataRequested: true } activity)
                    activity.SetTag(DispatchTelemetry.StreamItemsTag, _items);

                if (_fault is null)
                    _scope.Complete(error: null);
                else
                    _scope.Fail(_fault, IsCancellation(_fault, _dispatchToken, _enumeratorToken));
            }
        }
    }
}
