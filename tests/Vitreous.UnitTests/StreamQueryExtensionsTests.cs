using NSubstitute;

namespace Vitreous.UnitTests;

public sealed class StreamQueryExtensionsTests
{
    // ── Test types ────────────────────────────────────────────────────────────

    private sealed record IntStreamQuery : IStreamQuery<int>;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<T> AsAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
            yield return item;
        await Task.CompletedTask;
    }

    private static ISender SenderReturning(params int[] items)
    {
        var stream = AsAsyncEnumerable(items);
        var sender = Substitute.For<ISender>();
        sender.CreateStream(Arg.Any<IntStreamQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result<IAsyncEnumerable<int>>.Ok(stream)));
        return sender;
    }

    private static ISender SenderReturningFailure(Error error)
    {
        var sender = Substitute.For<ISender>();
        sender.CreateStream(Arg.Any<IntStreamQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result<IAsyncEnumerable<int>>.Fail(error)));
        return sender;
    }

    private static ISender SenderReturningFaultedStream()
    {
        var sender = Substitute.For<ISender>();
        sender.CreateStream(Arg.Any<IntStreamQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result<IAsyncEnumerable<int>>.Ok(FaultedStream())));
        return sender;
    }

    private static async IAsyncEnumerable<int> FaultedStream()
    {
        yield return 1;
        await Task.CompletedTask;
        throw new InvalidOperationException("mid-stream failure");
    }

    // ── CollectStream ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectStream_SuccessfulStream_ReturnsAllItems()
    {
        var sender = SenderReturning(10, 20, 30);

        var result = await sender.CollectStream(new IntStreamQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(10, 20, 30);
    }

    [Fact]
    public async Task CollectStream_EmptyStream_ReturnsEmptyList()
    {
        var sender = SenderReturning();

        var result = await sender.CollectStream(new IntStreamQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectStream_HandlerReturnsFailure_PropagatesFailure()
    {
        var error = Error.Validation("Query was invalid.");
        var sender = SenderReturningFailure(error);

        var result = await sender.CollectStream(new IntStreamQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public async Task CollectStream_MidStreamException_ReturnsInternalError()
    {
        var sender = SenderReturningFaultedStream();

        var result = await sender.CollectStream(new IntStreamQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("INTERNAL");
        result.Error.Message.Should().Contain("Stream collection failed.");
        result.Error.Extensions.Should().ContainKey("exceptionType");
        result.Error.Extensions!["exceptionType"].Should().Be("InvalidOperationException");
    }

    // ── MapStream ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MapStream_ProjectsEachItem()
    {
        var source = Result<IAsyncEnumerable<int>>.Ok(AsAsyncEnumerable([1, 2, 3]));

        var mapped = source.MapStream(x => x * 10);

        mapped.IsSuccess.Should().BeTrue();
        var items = new List<int>();
        await foreach (var item in mapped.Value)
            items.Add(item);
        items.Should().Equal(10, 20, 30);
    }

    [Fact]
    public void MapStream_OnFailure_PassesThroughUntouched()
    {
        var error = Error.NotFound("not found");
        var failure = Result<IAsyncEnumerable<int>>.Fail(error);

        var mapped = failure.MapStream(x => x.ToString());

        mapped.IsFailure.Should().BeTrue();
        mapped.Error.Should().Be(error);
    }

    // ── FilterStream ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FilterStream_KeepsMatchingItems()
    {
        var source = Result<IAsyncEnumerable<int>>.Ok(AsAsyncEnumerable([1, 2, 3, 4, 5]));

        var filtered = source.FilterStream(x => x % 2 == 0);

        filtered.IsSuccess.Should().BeTrue();
        var items = new List<int>();
        await foreach (var item in filtered.Value)
            items.Add(item);
        items.Should().Equal(2, 4);
    }

    [Fact]
    public void FilterStream_OnFailure_PassesThroughUntouched()
    {
        var error = Error.Unauthorized("access denied");
        var failure = Result<IAsyncEnumerable<int>>.Fail(error);

        var filtered = failure.FilterStream(x => x > 0);

        filtered.IsFailure.Should().BeTrue();
        filtered.Error.Should().Be(error);
    }

    // ── TakeStream ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TakeStream_TruncatesToRequestedCount()
    {
        var source = Result<IAsyncEnumerable<int>>.Ok(AsAsyncEnumerable([10, 20, 30, 40, 50]));

        var taken = source.TakeStream(3);

        taken.IsSuccess.Should().BeTrue();
        var items = new List<int>();
        await foreach (var item in taken.Value)
            items.Add(item);
        items.Should().Equal(10, 20, 30);
    }

    [Fact]
    public async Task TakeStream_CountExceedsStreamLength_ReturnsAllItems()
    {
        var source = Result<IAsyncEnumerable<int>>.Ok(AsAsyncEnumerable([1, 2]));

        var taken = source.TakeStream(100);

        var items = new List<int>();
        await foreach (var item in taken.Value)
            items.Add(item);
        items.Should().Equal(1, 2);
    }

    [Fact]
    public void TakeStream_OnFailure_PassesThroughUntouched()
    {
        var error = Error.Internal("storage failure");
        var failure = Result<IAsyncEnumerable<int>>.Fail(error);

        var taken = failure.TakeStream(5);

        taken.IsFailure.Should().BeTrue();
        taken.Error.Should().Be(error);
    }

    // ── StreamQueryHandler base class ─────────────────────────────────────────

    private sealed record NumberQuery(int Max) : IStreamQuery<int>;

    private sealed class NumberStreamHandler : StreamQueryHandler<NumberQuery, int>
    {
        protected override async IAsyncEnumerable<int> Stream(
            NumberQuery query,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 1; i <= query.Max; i++)
            {
                ct.ThrowIfCancellationRequested();
                yield return i;
                await Task.CompletedTask;
            }
        }
    }

    private sealed class GuardedNumberStreamHandler : StreamQueryHandler<NumberQuery, int>
    {
        public override ValueTask<Result<IAsyncEnumerable<int>>> Handle(
            NumberQuery query,
            CancellationToken ct = default)
        {
            if (query.Max <= 0)
                return new(Result<IAsyncEnumerable<int>>.Fail(
                    Error.Validation("Max must be positive.")));

            return base.Handle(query, ct);
        }

        protected override async IAsyncEnumerable<int> Stream(
            NumberQuery query,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 1; i <= query.Max; i++)
            {
                yield return i;
                await Task.CompletedTask;
            }
        }
    }

    [Fact]
    public async Task StreamQueryHandler_DefaultHandle_ReturnsSuccessWrappingStream()
    {
        var handler = new NumberStreamHandler();

        var result = await handler.Handle(new NumberQuery(3));

        result.IsSuccess.Should().BeTrue();

        var items = new List<int>();
        await foreach (var item in result.Value)
            items.Add(item);

        items.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task StreamQueryHandler_Override_CanReturnFailureBeforeStream()
    {
        var handler = new GuardedNumberStreamHandler();

        var result = await handler.Handle(new NumberQuery(Max: 0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public async Task StreamQueryHandler_Override_HappyPath_YieldsItems()
    {
        var handler = new GuardedNumberStreamHandler();

        var result = await handler.Handle(new NumberQuery(Max: 4));

        result.IsSuccess.Should().BeTrue();
        var items = new List<int>();
        await foreach (var item in result.Value)
            items.Add(item);
        items.Should().Equal(1, 2, 3, 4);
    }
}
