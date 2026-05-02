namespace Vitreous.CachingTests;

public sealed class CacheInvalidationBehaviorTests
{
    // ── Test doubles ───────────────────────────────────────────────────────────

    private sealed record DeleteItemCommand(int Id) : ICommand<bool>, IInvalidatesCache
    {
        public IReadOnlyList<string> CacheKeys      => [$"item:{Id}"];
        public IReadOnlyList<string> InvalidatesTags => ["items"];
    }

    private sealed record DeleteWithTagsOnly(int Id) : ICommand<bool>, IInvalidatesCache
    {
        public IReadOnlyList<string> CacheKeys       => [];
        public IReadOnlyList<string> InvalidatesTags  => [$"group:{Id}"];
    }

    private sealed record PlainCommand(int Id) : ICommand<bool>;

    private sealed record FailingCommand(int Id) : ICommand<bool>, IInvalidatesCache
    {
        public IReadOnlyList<string> CacheKeys       => [$"item:{Id}"];
        public IReadOnlyList<string> InvalidatesTags  => ["items"];
    }

    private static MessageHandlerDelegate<TMsg, TResult> Handler<TMsg, TResult>(
        Func<TMsg, Result<TResult>> fn) =>
        (msg, _) => new ValueTask<Result<TResult>>(fn(msg));

    private static CacheInvalidationBehavior<TMsg, TResult> CreateBehavior<TMsg, TResult>(
        FakeVitreousCache cache) =>
        new(cache, NullLogger<CacheInvalidationBehavior<TMsg, TResult>>.Instance);

    // ── Keys removed on success ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SuccessfulCommand_RemovesDeclaredCacheKeys()
    {
        var cache = new FakeVitreousCache();
        cache.Seed("item:3", "existing");
        var behavior = CreateBehavior<DeleteItemCommand, bool>(cache);

        var result = await behavior.Handle(
            new DeleteItemCommand(3),
            Handler<DeleteItemCommand, bool>(_ => Result<bool>.Ok(true)),
            default);

        result.IsSuccess.Should().BeTrue();
        cache.RemoveCalls.Should().Contain("item:3");
        cache.Contains("item:3").Should().BeFalse();
    }

    // ── Tags forwarded on success ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_SuccessfulCommand_InvokesRemoveByTagForEachTag()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<DeleteItemCommand, bool>(cache);

        await behavior.Handle(
            new DeleteItemCommand(1),
            Handler<DeleteItemCommand, bool>(_ => Result<bool>.Ok(true)),
            default);

        cache.TagCalls.Should().Contain("items");
    }

    // ── Tags-only invalidation ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_TagsOnlyCommand_InvokesRemoveByTagWithoutKeyRemoval()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<DeleteWithTagsOnly, bool>(cache);

        await behavior.Handle(
            new DeleteWithTagsOnly(5),
            Handler<DeleteWithTagsOnly, bool>(_ => Result<bool>.Ok(true)),
            default);

        cache.RemoveCalls.Should().BeEmpty();
        cache.TagCalls.Should().ContainSingle("group:5");
    }

    // ── Nothing removed on failure ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_FailedCommand_DoesNotRemoveCacheEntries()
    {
        var cache = new FakeVitreousCache();
        cache.Seed("item:9", "should-survive");
        var behavior = CreateBehavior<FailingCommand, bool>(cache);

        var result = await behavior.Handle(
            new FailingCommand(9),
            Handler<FailingCommand, bool>(_ => Error.Internal("boom")),
            default);

        result.IsFailure.Should().BeTrue();
        cache.RemoveCalls.Should().BeEmpty("nothing must be invalidated when the command fails");
        cache.TagCalls.Should().BeEmpty();
        cache.Contains("item:9").Should().BeTrue("existing cache must survive a failed command");
    }

    // ── Non-invalidating command passes through ────────────────────────────────

    [Fact]
    public async Task Handle_NonInvalidatingCommand_PassesThrough()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<PlainCommand, bool>(cache);

        var result = await behavior.Handle(
            new PlainCommand(1),
            Handler<PlainCommand, bool>(_ => Result<bool>.Ok(true)),
            default);

        result.IsSuccess.Should().BeTrue();
        cache.RemoveCalls.Should().BeEmpty();
        cache.TagCalls.Should().BeEmpty();
    }

    // ── Multiple keys removed atomically ──────────────────────────────────────

    [Fact]
    public async Task Handle_MultipleKeys_AllKeysRemoved()
    {
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior<MultiKeyCommand, bool>(cache);
        cache.Seed("order:1", "a");
        cache.Seed("order:2", "b");
        cache.Seed("order:list", "c");

        await behavior.Handle(
            new MultiKeyCommand(),
            Handler<MultiKeyCommand, bool>(_ => Result<bool>.Ok(true)),
            default);

        cache.RemoveCalls.Should().BeEquivalentTo(["order:1", "order:2", "order:list"]);
        cache.Contains("order:1").Should().BeFalse();
        cache.Contains("order:2").Should().BeFalse();
        cache.Contains("order:list").Should().BeFalse();
    }

    private sealed record MultiKeyCommand : ICommand<bool>, IInvalidatesCache
    {
        public IReadOnlyList<string> CacheKeys      => ["order:1", "order:2", "order:list"];
        public IReadOnlyList<string> InvalidatesTags => [];
    }
}
