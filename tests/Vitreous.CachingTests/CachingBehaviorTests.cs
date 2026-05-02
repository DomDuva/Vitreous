namespace Vitreous.CachingTests;

public sealed class CachingBehaviorTests
{
    // ── Test doubles ───────────────────────────────────────────────────────────

    private sealed record GetItemQuery(int Id) : ICacheableQuery<string>
    {
        public string CacheKey          => $"item:{Id}";
        public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);
        public TimeSpan? SlidingExpiration  => null;
    }

    private sealed record PlainQuery(int Id) : IQuery<string>;

    private static MessageHandlerDelegate<TMsg, TResult> Handler<TMsg, TResult>(
        Func<TMsg, Result<TResult>> fn) =>
        (msg, _) => new ValueTask<Result<TResult>>(fn(msg));

    private static CachingBehavior<TMsg, TResult> CreateBehavior<TMsg, TResult>(
        FakeVitreousCache cache) =>
        new(cache, NullLogger<CachingBehavior<TMsg, TResult>>.Instance);

    // ── Cache hit ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CacheHit_ReturnsValueWithoutCallingHandler()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<GetItemQuery, string>(cache);
        var query    = new GetItemQuery(42);
        cache.Seed("item:42", "cached-value");

        var handlerCalled = false;
        var result = await behavior.Handle(
            query,
            Handler<GetItemQuery, string>(_ => { handlerCalled = true; return Result<string>.Ok("fresh"); }),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("cached-value");
        handlerCalled.Should().BeFalse("handler must not be called on a cache hit");
        cache.GetCalls.Should().ContainSingle("item:42");
        cache.SetCalls.Should().BeEmpty();
    }

    // ── Cache miss → handler called, result stored ─────────────────────────────

    [Fact]
    public async Task Handle_CacheMiss_CallsHandlerAndStoresResult()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<GetItemQuery, string>(cache);
        var query    = new GetItemQuery(7);

        var result = await behavior.Handle(
            query,
            Handler<GetItemQuery, string>(_ => Result<string>.Ok("fresh-value")),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("fresh-value");
        cache.GetCalls.Should().ContainSingle("item:7");
        cache.SetCalls.Should().ContainSingle("item:7");
        cache.Contains("item:7").Should().BeTrue();
    }

    // ── Failure is not cached ──────────────────────────────────────────────────

    [Fact]
    public async Task Handle_HandlerReturnsFailure_ResultNotCached()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<GetItemQuery, string>(cache);
        var query    = new GetItemQuery(99);

        var result = await behavior.Handle(
            query,
            Handler<GetItemQuery, string>(_ => Error.NotFound("not found")),
            default);

        result.IsFailure.Should().BeTrue();
        cache.SetCalls.Should().BeEmpty("failures must never be cached");
        cache.Contains("item:99").Should().BeFalse();
    }

    // ── Non-cacheable query passes through unchanged ───────────────────────────

    [Fact]
    public async Task Handle_NonCacheableQuery_PassesThrough()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<PlainQuery, string>(cache);
        var query    = new PlainQuery(1);

        var result = await behavior.Handle(
            query,
            Handler<PlainQuery, string>(_ => Result<string>.Ok("direct")),
            default);

        result.Value.Should().Be("direct");
        cache.GetCalls.Should().BeEmpty();
        cache.SetCalls.Should().BeEmpty();
    }

    // ── Cache read failure falls back to handler ───────────────────────────────

    [Fact]
    public async Task Handle_CacheReadThrows_FallsBackToHandlerAndStoresResult()
    {
        var faultyCache = new ThrowingOnReadCache();
        var behavior    = new CachingBehavior<GetItemQuery, string>(
            faultyCache, NullLogger<CachingBehavior<GetItemQuery, string>>.Instance);
        var query = new GetItemQuery(5);

        var result = await behavior.Handle(
            query,
            Handler<GetItemQuery, string>(_ => Result<string>.Ok("fallback")),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("fallback");
        faultyCache.SetCalled.Should().BeTrue();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private sealed class ThrowingOnReadCache : IVitreousCache
    {
        public bool SetCalled { get; private set; }

        public ValueTask<(bool Hit, T? Value)> GetAsync<T>(string key, CancellationToken ct = default)
            => throw new InvalidOperationException("simulated cache failure");

        public ValueTask SetAsync<T>(string key, T value, IReadOnlyList<string> tags,
            DistributedCacheEntryOptions options, CancellationToken ct = default)
        {
            SetCalled = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
