namespace Vitreous.UnitTests.Pipeline;

public sealed class PipelineExecutorTests
{
    // ── Test message types ────────────────────────────────────────────────────

    private sealed record TestQuery : IQuery<int>;
    private sealed record TestVoidCommand : ICommand;
    private sealed record TestNotification : INotification;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MessageHandlerDelegate<TestQuery, int> ConstantTerminal(int value) =>
        (_, _) => ValueTask.FromResult(Result<int>.Ok(value));

    private static MessageHandlerDelegate<TestQuery, int> LoggingTerminal(List<string> log, int value) =>
        (_, _) => { log.Add("terminal"); return ValueTask.FromResult(Result<int>.Ok(value)); };

    private static CommandHandlerDelegate<TestVoidCommand> VoidTerminal(List<string> log) =>
        (_, _) => { log.Add("terminal"); return ValueTask.FromResult(Result.Success); };

    private static NotificationHandlerDelegate<TestNotification> NotificationTerminal(List<string> log) =>
        (_, _) => { log.Add("terminal"); return ValueTask.CompletedTask; };

    // ── Execute<TMessage, TResult> ────────────────────────────────────────────

    [Fact]
    public async Task Execute_NoBehaviors_CallsTerminalAndReturnsItsResult()
    {
        var result = await PipelineExecutor.Execute(
            new TestQuery(),
            ConstantTerminal(99),
            [],
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(99);
    }

    [Fact]
    public async Task Execute_SingleBehavior_WrapsTerminal()
    {
        var log = new List<string>();
        var behaviors = new[] { new OrderBehavior<TestQuery, int>(log, "B1") };

        await PipelineExecutor.Execute(
            new TestQuery(), LoggingTerminal(log, 1), behaviors, CancellationToken.None);

        log.Should().Equal("B1:before", "terminal", "B1:after");
    }

    [Fact]
    public async Task Execute_MultipleBehaviors_FirstRegisteredIsOutermostWrap()
    {
        var log = new List<string>();
        var behaviors = new IPipelineBehavior<TestQuery, int>[]
        {
            new OrderBehavior<TestQuery, int>(log, "B1"),
            new OrderBehavior<TestQuery, int>(log, "B2"),
            new OrderBehavior<TestQuery, int>(log, "B3"),
        };

        await PipelineExecutor.Execute(
            new TestQuery(), LoggingTerminal(log, 0), behaviors, CancellationToken.None);

        log.Should().Equal(
            "B1:before", "B2:before", "B3:before",
            "terminal",
            "B3:after", "B2:after", "B1:after");
    }

    [Fact]
    public async Task Execute_BehaviorShortCircuits_TerminalIsNotCalled()
    {
        var terminalCalled = false;
        var shortCircuitError = new Error("BLOCKED", "short-circuited");

        var behaviors = new IPipelineBehavior<TestQuery, int>[]
        {
            new ShortCircuitBehavior<TestQuery, int>(shortCircuitError)
        };

        var result = await PipelineExecutor.Execute(
            new TestQuery(),
            (_, _) => { terminalCalled = true; return ValueTask.FromResult(Result<int>.Ok(1)); },
            behaviors,
            CancellationToken.None);

        terminalCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(shortCircuitError);
    }

    [Fact]
    public async Task Execute_BehaviorReceivesMessage_UnmodifiedFromCaller()
    {
        TestQuery? received = null;
        var query = new TestQuery();

        await PipelineExecutor.Execute(
            query,
            (msg, _) => { received = msg; return ValueTask.FromResult(Result<int>.Ok(0)); },
            [],
            CancellationToken.None);

        received.Should().BeSameAs(query);
    }

    [Fact]
    public async Task Execute_CancellationToken_PropagatedToTerminal()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken received = default;

        await PipelineExecutor.Execute(
            new TestQuery(),
            (_, ct) => { received = ct; return ValueTask.FromResult(Result<int>.Ok(0)); },
            [],
            cts.Token);

        received.Should().Be(cts.Token);
    }

    // ── ExecuteVoid ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteVoid_NoBehaviors_CallsTerminal()
    {
        var log = new List<string>();

        var result = await PipelineExecutor.ExecuteVoid(
            new TestVoidCommand(), VoidTerminal(log), [], CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        log.Should().Equal("terminal");
    }

    [Fact]
    public async Task ExecuteVoid_WithBehavior_WrapsTerminal()
    {
        var log = new List<string>();

        await PipelineExecutor.ExecuteVoid(
            new TestVoidCommand(),
            VoidTerminal(log),
            new[] { new VoidOrderBehavior(log, "B1") },
            CancellationToken.None);

        log.Should().Equal("B1:before", "terminal", "B1:after");
    }

    [Fact]
    public async Task ExecuteVoid_MultipleBehaviors_OrderPreserved()
    {
        var log = new List<string>();

        await PipelineExecutor.ExecuteVoid(
            new TestVoidCommand(),
            VoidTerminal(log),
            new ICommandPipelineBehavior<TestVoidCommand>[]
            {
                new VoidOrderBehavior(log, "B1"),
                new VoidOrderBehavior(log, "B2"),
            },
            CancellationToken.None);

        log.Should().Equal("B1:before", "B2:before", "terminal", "B2:after", "B1:after");
    }

    // ── ExecuteNotification ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteNotification_NoBehaviors_CallsTerminal()
    {
        var log = new List<string>();

        await PipelineExecutor.ExecuteNotification(
            new TestNotification(), NotificationTerminal(log), [], CancellationToken.None);

        log.Should().Equal("terminal");
    }

    [Fact]
    public async Task ExecuteNotification_WithBehavior_WrapsTerminal()
    {
        var log = new List<string>();

        await PipelineExecutor.ExecuteNotification(
            new TestNotification(),
            NotificationTerminal(log),
            new[] { new NotificationOrderBehavior(log, "B1") },
            CancellationToken.None);

        log.Should().Equal("B1:before", "terminal", "B1:after");
    }

    [Fact]
    public async Task ExecuteNotification_MultipleBehaviors_OrderPreserved()
    {
        var log = new List<string>();

        await PipelineExecutor.ExecuteNotification(
            new TestNotification(),
            NotificationTerminal(log),
            new INotificationPipelineBehavior<TestNotification>[]
            {
                new NotificationOrderBehavior(log, "B1"),
                new NotificationOrderBehavior(log, "B2"),
            },
            CancellationToken.None);

        log.Should().Equal("B1:before", "B2:before", "terminal", "B2:after", "B1:after");
    }

    // ── Inner behavior implementations ────────────────────────────────────────

    private sealed class OrderBehavior<TMessage, TResult>(List<string> log, string name)
        : IPipelineBehavior<TMessage, TResult>
    {
        public async ValueTask<Result<TResult>> Handle(
            TMessage message,
            MessageHandlerDelegate<TMessage, TResult> next,
            CancellationToken ct)
        {
            log.Add($"{name}:before");
            var result = await next(message, ct);
            log.Add($"{name}:after");
            return result;
        }
    }

    private sealed class ShortCircuitBehavior<TMessage, TResult>(Error error)
        : IPipelineBehavior<TMessage, TResult>
    {
        public ValueTask<Result<TResult>> Handle(
            TMessage message,
            MessageHandlerDelegate<TMessage, TResult> next,
            CancellationToken ct) =>
            ValueTask.FromResult(Result<TResult>.Fail(error));
    }

    private sealed class VoidOrderBehavior(List<string> log, string name)
        : ICommandPipelineBehavior<TestVoidCommand>
    {
        public async ValueTask<Result> Handle(
            TestVoidCommand command,
            CommandHandlerDelegate<TestVoidCommand> next,
            CancellationToken ct)
        {
            log.Add($"{name}:before");
            var result = await next(command, ct);
            log.Add($"{name}:after");
            return result;
        }
    }

    private sealed class NotificationOrderBehavior(List<string> log, string name)
        : INotificationPipelineBehavior<TestNotification>
    {
        public async ValueTask Handle(
            TestNotification notification,
            NotificationHandlerDelegate<TestNotification> next,
            CancellationToken ct)
        {
            log.Add($"{name}:before");
            await next(notification, ct);
            log.Add($"{name}:after");
        }
    }
}
