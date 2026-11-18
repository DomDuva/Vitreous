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

    // ── Null cached value ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CachedValueIsNull_TreatedAsMiss()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<GetItemQuery, string>(cache);
        cache.SeedNullHit("item:1");

        var result = await behavior.Handle(
            new GetItemQuery(1),
            Handler<GetItemQuery, string>(_ => Result<string>.Ok("fresh")),
            default);

        result.Value.Should().Be("fresh");
        cache.SetCalls.Should().ContainSingle("item:1");
    }

    // ── Default expiration ─────────────────────────────────────────────────────

    private sealed record NoExpiryQuery(int Id) : ICacheableQuery<string>
    {
        public string CacheKey              => $"no-expiry:{Id}";
        public TimeSpan? AbsoluteExpiration => null;
        public TimeSpan? SlidingExpiration  => null;
    }

    private sealed record SlidingQuery(int Id) : ICacheableQuery<string>
    {
        public string CacheKey              => $"sliding:{Id}";
        public TimeSpan? AbsoluteExpiration => null;
        public TimeSpan? SlidingExpiration  => TimeSpan.FromMinutes(1);
    }

    [Fact]
    public async Task Handle_QueryWithoutExpiry_UsesDefaultAbsoluteExpiration()
    {
        var cache    = new FakeVitreousCache();
        var behavior = new CachingBehavior<NoExpiryQuery, string>(
            cache, NullLogger<CachingBehavior<NoExpiryQuery, string>>.Instance,
            new Vitreous.Caching.VitreousCachingOptions { DefaultAbsoluteExpiration = TimeSpan.FromMinutes(10) });

        await behavior.Handle(new NoExpiryQuery(1), Handler<NoExpiryQuery, string>(_ => "v"), default);

        cache.SetOptions.Should().ContainSingle()
            .Which.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Handle_QueryWithOwnExpiry_IgnoresDefaultAbsoluteExpiration()
    {
        var cache   = new FakeVitreousCache();
        var options = new Vitreous.Caching.VitreousCachingOptions { DefaultAbsoluteExpiration = TimeSpan.FromMinutes(10) };

        await new CachingBehavior<GetItemQuery, string>(cache, NullLogger<CachingBehavior<GetItemQuery, string>>.Instance, options)
            .Handle(new GetItemQuery(1), Handler<GetItemQuery, string>(_ => "v"), default);
        await new CachingBehavior<SlidingQuery, string>(cache, NullLogger<CachingBehavior<SlidingQuery, string>>.Instance, options)
            .Handle(new SlidingQuery(1), Handler<SlidingQuery, string>(_ => "v"), default);

        cache.SetOptions[0].AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(5));
        cache.SetOptions[1].AbsoluteExpirationRelativeToNow.Should().BeNull();
        cache.SetOptions[1].SlidingExpiration.Should().Be(TimeSpan.FromMinutes(1));
    }

    // ── Stampede protection ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ConcurrentMissesForSameKey_RunTheHandlerOnce()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<GetItemQuery, string>(cache);
        var gate     = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls    = 0;

        MessageHandlerDelegate<GetItemQuery, string> slow = async (_, _) =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return "loaded";
        };

        var pending = Enumerable.Range(0, 10)
            .Select(_ => behavior.Handle(new GetItemQuery(1), slow, default).AsTask())
            .ToList();
        gate.SetResult();
        var results = await Task.WhenAll(pending);

        calls.Should().Be(1);
        results.Should().AllSatisfy(r => r.Value.Should().Be("loaded"));
        cache.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_StampedeProtectionDisabled_EveryMissRunsTheHandler()
    {
        var cache    = new FakeVitreousCache();
        var behavior = new CachingBehavior<GetItemQuery, string>(
            cache, NullLogger<CachingBehavior<GetItemQuery, string>>.Instance,
            new Vitreous.Caching.VitreousCachingOptions { EnableStampedeProtection = false });
        var gate  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        MessageHandlerDelegate<GetItemQuery, string> slow = async (_, _) =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return "loaded";
        };

        var pending = Enumerable.Range(0, 5)
            .Select(_ => behavior.Handle(new GetItemQuery(1), slow, default).AsTask())
            .ToList();
        gate.SetResult();
        await Task.WhenAll(pending);

        calls.Should().Be(5);
    }

    [Fact]
    public async Task Handle_CallerRunningTheHandlerIsCanceled_WaitingCallerLoadsItself()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<GetItemQuery, string>(cache);
        var calls    = 0;

        MessageHandlerDelegate<GetItemQuery, string> waitForCancel = async (_, token) =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(Timeout.Infinite, token);
            return "never";
        };
        MessageHandlerDelegate<GetItemQuery, string> quick = (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return new ValueTask<Result<string>>("loaded");
        };

        using var leaderCts = new CancellationTokenSource();
        var leader = behavior.Handle(new GetItemQuery(1), waitForCancel, leaderCts.Token).AsTask();
        var waiter = behavior.Handle(new GetItemQuery(1), quick, default).AsTask();
        leaderCts.Cancel();

        await FluentActions.Awaiting(() => leader).Should().ThrowAsync<OperationCanceledException>();
        (await waiter).Value.Should().Be("loaded");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Handle_DifferentKeys_AreNotShared()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<GetItemQuery, string>(cache);

        var a = await behavior.Handle(new GetItemQuery(1), Handler<GetItemQuery, string>(q => $"v{q.Id}"), default);
        var b = await behavior.Handle(new GetItemQuery(2), Handler<GetItemQuery, string>(q => $"v{q.Id}"), default);

        (a.Value, b.Value).Should().Be(("v1", "v2"));
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
