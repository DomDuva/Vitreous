namespace Vitreous.FluentValidationTests;

public sealed class ValidationBehaviorTests
{
    // ── Test doubles ───────────────────────────────────────────────────────────

    private sealed record Order(string Customer, int Quantity);

    private sealed class CustomerValidator : AbstractValidator<Order>
    {
        public CustomerValidator()
        {
            RuleFor(o => o.Customer).NotEmpty().WithMessage("Customer is required.");
            RuleFor(o => o.Customer).MaximumLength(4).WithMessage("Customer is too long.");
        }
    }

    private sealed class QuantityValidator : AbstractValidator<Order>
    {
        public QuantityValidator()
        {
            RuleFor(o => o.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
            RuleFor(o => o.Customer).Must(c => c != "admin").WithMessage("Customer is reserved.");
        }
    }

    /// <summary>A validator with an async rule that records the token it was given.</summary>
    private sealed class TokenValidator : AbstractValidator<Order>
    {
        public CancellationToken Seen { get; private set; }

        public TokenValidator() =>
            RuleFor(o => o.Customer).MustAsync((_, ct) =>
            {
                Seen = ct;
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(true);
            });
    }

    private static ValidationBehavior<Order, int> CreateBehavior(params IValidator<Order>[] validators) =>
        new(validators);

    private static MessageHandlerDelegate<Order, int> Next(Action onCall) =>
        (_, _) =>
        {
            onCall();
            return new(42);
        };

    // ── Pass-through ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoValidators_CallsNextDirectly()
    {
        var calls = 0;
        var behavior = CreateBehavior();

        var pending = behavior.Handle(new Order("", 0), Next(() => calls++));

        // No validators: the behavior returns next's ValueTask, without awaiting it.
        pending.IsCompletedSuccessfully.Should().BeTrue();
        (await pending).Value.Should().Be(42);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ValidMessage_CallsNext()
    {
        var calls = 0;
        var behavior = CreateBehavior(new CustomerValidator(), new QuantityValidator());

        var result = await behavior.Handle(new Order("ada", 2), Next(() => calls++));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        calls.Should().Be(1);
    }

    // ── Failures ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_InvalidMessage_ReturnsValidationErrorWithoutCallingNext()
    {
        var calls = 0;
        var behavior = CreateBehavior(new QuantityValidator());

        var result = await behavior.Handle(new Order("ada", 0), Next(() => calls++));

        calls.Should().Be(0);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
        result.Error.Message.Should().Be("Validation failed.");
        Failures(result.Error).Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["Quantity"] = ["Quantity must be greater than zero."],
        });
    }

    [Fact]
    public async Task Handle_SeveralValidators_GroupsEveryFailureByProperty()
    {
        var behavior = CreateBehavior(new CustomerValidator(), new QuantityValidator());

        var result = await behavior.Handle(new Order("admin", -1), Next(() => { }));

        var failures = Failures(result.Error);
        failures.Keys.Should().BeEquivalentTo("Customer", "Quantity");
        failures["Customer"].Should().Equal("Customer is too long.", "Customer is reserved.");
        failures["Quantity"].Should().Equal("Quantity must be greater than zero.");
    }

    // ── Cancellation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PassesTheTokenToValidators()
    {
        using var cts = new CancellationTokenSource();
        var validator = new TokenValidator();
        var behavior = CreateBehavior(validator);

        await behavior.Handle(new Order("ada", 1), Next(() => { }), cts.Token);

        validator.Seen.Should().Be(cts.Token);
    }

    [Fact]
    public async Task Handle_CanceledToken_ThrowsWithoutCallingNext()
    {
        var calls = 0;
        var behavior = CreateBehavior(new TokenValidator());

        var act = async () => await behavior.Handle(new Order("ada", 1), Next(() => calls++), new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(0);
    }

    internal static IReadOnlyDictionary<string, string[]> Failures(Error error) =>
        error.Extensions!["failures"].Should().BeAssignableTo<IReadOnlyDictionary<string, string[]>>().Subject;
}
