using System.Collections.Concurrent;

namespace Vitreous.CachingTests.Fakes;

/// <summary>
/// In-memory <see cref="IVitreousCache"/> for unit tests.
/// Tracks every operation so tests can assert call counts and arguments.
/// </summary>
public sealed class FakeVitreousCache : IVitreousCache
{
    private readonly ConcurrentDictionary<string, object?> _store = new();

    // ── Operation log ───────────────────────────────────────────────────────
    public List<string> GetCalls    { get; } = [];
    public List<string> SetCalls    { get; } = [];
    public List<string> RemoveCalls { get; } = [];
    public List<string> TagCalls    { get; } = [];

    /// <summary>Pre-populate the cache to simulate a hit.</summary>
    public void Seed<T>(string key, T value) => _store[key] = value;

    public ValueTask<(bool Hit, T? Value)> GetAsync<T>(string key, CancellationToken ct = default)
    {
        GetCalls.Add(key);
        if (_store.TryGetValue(key, out var raw) && raw is T typed)
            return new((true, typed));
        return new((false, default));
    }

    public ValueTask SetAsync<T>(
        string key,
        T value,
        IReadOnlyList<string> tags,
        DistributedCacheEntryOptions options,
        CancellationToken ct = default)
    {
        SetCalls.Add(key);
        _store[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        RemoveCalls.Add(key);
        _store.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        TagCalls.Add(tag);
        return ValueTask.CompletedTask;
    }

    /// <summary>Whether the given key currently exists in the store.</summary>
    public bool Contains(string key) => _store.ContainsKey(key);
}
