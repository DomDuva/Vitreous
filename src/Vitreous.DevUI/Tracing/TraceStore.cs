using System.Collections.Concurrent;

namespace Vitreous.DevUI.Tracing;

/// <summary>
/// Thread-safe ring buffer that holds the last <see cref="Capacity"/> execution traces.
/// New entries push out the oldest when the buffer is full.
/// Optimized for a high-frequency write path and an infrequent bulk-read path (SSE replay).
/// </summary>
public sealed class TraceStore
{
    private readonly ExecutionTrace?[] _buffer;
    private readonly object _writeLock = new();
    private int _head;   // next write position (mod Capacity)
    private int _count;  // number of valid entries (capped at Capacity)

    /// <summary>Event raised on the thread pool after each new trace is appended.</summary>
    public event Action<ExecutionTrace>? TraceAdded;

    /// <summary>Maximum number of traces retained. Configurable via <see cref="VitreousDevUIOptions"/>.</summary>
    public int Capacity { get; }

    public TraceStore(int capacity = 500)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be > 0.");

        Capacity = capacity;
        _buffer  = new ExecutionTrace?[capacity];
    }

    /// <summary>
    /// Appends a trace to the ring buffer and fires <see cref="TraceAdded"/>.
    /// Thread-safe.
    /// </summary>
    public void Append(ExecutionTrace trace)
    {
        lock (_writeLock)
        {
            _buffer[_head] = trace;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        // Fire event outside the lock — subscribers may take time
        TraceAdded?.Invoke(trace);
    }

    /// <summary>
    /// Returns all retained traces in chronological order (oldest first).
    /// Thread-safe snapshot — callers get a consistent view at the moment of the call.
    /// </summary>
    public IReadOnlyList<ExecutionTrace> GetAll()
    {
        lock (_writeLock)
        {
            if (_count == 0) return Array.Empty<ExecutionTrace>();

            var result = new ExecutionTrace[_count];

            if (_count < Capacity)
            {
                // Buffer not yet full — data starts at index 0
                Array.Copy(_buffer, result, _count);
            }
            else
            {
                // Buffer full — oldest entry is at _head
                var tail = Capacity - _head;   // entries from _head to end of array
                Array.Copy(_buffer, _head, result, 0, tail);
                Array.Copy(_buffer, 0, result, tail, _head);
            }

            return result;
        }
    }

    /// <summary>Returns the number of traces currently stored.</summary>
    public int Count
    {
        get { lock (_writeLock) return _count; }
    }
}
