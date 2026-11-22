namespace Vitreous.FluentValidationTests;

public sealed class StreamValidationBehaviorTests
{
    private sealed record Range(int Count) : IStreamQuery<int>;

    private sealed class RangeValidator : AbstractValidator<Range>
    {
        public RangeValidator() => RuleFor(r => r.Count).GreaterThan(0).WithMessage("Count must be positive.");
    }

    private static StreamQueryHandlerDelegate<Range, int> Next(Action onCall) =>
        (query, _) =>
        {
            onCall();
            return new(Result<IAsyncEnumerable<int>>.Ok(Items(query.Count)));
        };

    private static async IAsyncEnumerable<int> Items(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }

    [Fact]
    public async Task Handle_NoValidators_CallsNextDirectly()
    {
        var calls = 0;
        var behavior = new StreamValidationBehavior<Range, int>([]);

        var pending = behavior.Handle(new Range(0), Next(() => calls++));

        pending.IsCompletedSuccessfully.Should().BeTrue();
        (await pending).IsSuccess.Should().BeTrue();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ValidQuery_OpensTheStream()
    {
        var behavior = new StreamValidationBehavior<Range, int>([new RangeValidator()]);

        var result = await behavior.Handle(new Range(3), Next(() => { }));

        var items = new List<int>();
        await foreach (var item in result.Value)
            items.Add(item);
        items.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Handle_InvalidQuery_FailsBeforeTheStreamOpens()
    {
        var calls = 0;
        var behavior = new StreamValidationBehavior<Range, int>([new RangeValidator()]);

        var result = await behavior.Handle(new Range(0), Next(() => calls++));

        calls.Should().Be(0);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
        ValidationBehaviorTests.Failures(result.Error)["Count"].Should().Equal("Count must be positive.");
    }
}
