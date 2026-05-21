using Vitreous.Internal;

namespace Vitreous.UnitTests.Pipeline;

public sealed class ParallelPublisherTests
{
    private sealed record TestNotification(int Value) : INotification;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Vitreous.Internal.ParallelPublisher>();
        configure(services);
        return services.BuildServiceProvider();
    }

    // ── Fan-out correctness ───────────────────────────────────────────────────

    [Fact]
    public async Task Publish_ZeroHandlers_CompletesWithoutError()
    {
        var provider = BuildProvider(_ => { });
        var publisher = provider.GetRequiredService<Vitreous.Internal.ParallelPublisher>();

        Func<Task> act = () => publisher.Publish(new TestNotification(1)).AsTask();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Publish_SingleHandler_HandlerReceivesNotification()
    {
        var received = new List<int>();
        var provider = BuildProvider(s =>
            s.AddSingleton<INotificationHandler<TestNotification>>(new CaptureHandler(received)));

        var publisher = provider.GetRequiredService<Vitreous.Internal.ParallelPublisher>();
        await publisher.Publish(new TestNotification(42));

        received.Should().ContainSingle().Which.Should().Be(42);
    }

    [Fact]
    public async Task Publish_MultipleHandlers_AllHandlersInvoked()
    {
        var received = new List<int>();
        var provider = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(new CaptureHandler(received));
            s.AddSingleton<INotificationHandler<TestNotification>>(new CaptureHandler(received));
            s.AddSingleton<INotificationHandler<TestNotification>>(new CaptureHandler(received));
        });

        var publisher = provider.GetRequiredService<Vitreous.Internal.ParallelPublisher>();
        await publisher.Publish(new TestNotification(7));

        received.Should().HaveCount(3);
        received.Should().AllSatisfy(v => v.Should().Be(7));
    }

    // ── Parallel semantics ────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_MultipleHandlers_RunConcurrently()
    {
        var allReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reachedCount = 0;
        const int totalHandlers = 2;

        ValueTask HandleAsync(TestNotification _, CancellationToken ct)
        {
            if (Interlocked.Increment(ref reachedCount) == totalHandlers)
                allReached.TrySetResult();
            return new ValueTask(allReached.Task.WaitAsync(ct));
        }

        var provider = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(new FuncHandler(HandleAsync));
            s.AddSingleton<INotificationHandler<TestNotification>>(new FuncHandler(HandleAsync));
        });

        var publisher = provider.GetRequiredService<Vitreous.Internal.ParallelPublisher>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await publisher.Publish(new TestNotification(1), cts.Token);

        reachedCount.Should().Be(totalHandlers);
    }

    // ── Wait-all-then-fail behaviour ──────────────────────────────────────────

    [Fact]
    public async Task Publish_OneHandlerThrows_OtherHandlersStillRun()
    {
        var received = new List<int>();
        var provider = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(new ThrowingHandler());
            s.AddSingleton<INotificationHandler<TestNotification>>(new CaptureHandler(received));
        });

        var publisher = provider.GetRequiredService<Vitreous.Internal.ParallelPublisher>();
        try { await publisher.Publish(new TestNotification(42)); } catch { /* expected */ }

        received.Should().ContainSingle().Which.Should().Be(42);
    }

    [Fact]
    public async Task Publish_OneHandlerThrows_AggregateExceptionThrown()
    {
        var provider = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(new ThrowingHandler());
            s.AddSingleton<INotificationHandler<TestNotification>>(new CaptureHandler(new List<int>()));
        });

        var publisher = provider.GetRequiredService<Vitreous.Internal.ParallelPublisher>();
        Func<Task> act = () => publisher.Publish(new TestNotification(1)).AsTask();

        var ex = await act.Should().ThrowAsync<AggregateException>();
        ex.Which.InnerExceptions.Should().ContainSingle();
        ex.Which.InnerExceptions[0].Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("handler failure");
    }

    [Fact]
    public async Task Publish_AllHandlersThrow_AllExceptionsInAggregateException()
    {
        var provider = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(new ThrowingHandler());
            s.AddSingleton<INotificationHandler<TestNotification>>(new ThrowingHandler());
        });

        var publisher = provider.GetRequiredService<Vitreous.Internal.ParallelPublisher>();
        Func<Task> act = () => publisher.Publish(new TestNotification(1)).AsTask();

        var ex = await act.Should().ThrowAsync<AggregateException>();
        ex.Which.InnerExceptions.Should().HaveCount(2)
            .And.AllSatisfy(e => e.Should().BeOfType<InvalidOperationException>());
    }

    // ── Notification pipeline behaviors ───────────────────────────────────────

    [Fact]
    public async Task Publish_WithBehavior_BehaviorWrapsAllHandlers()
    {
        var log = new List<string>();
        var received = new List<int>();

        var provider = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(new CaptureHandler(received));
            s.AddSingleton<INotificationPipelineBehavior<TestNotification>>(
                new LoggingNotificationBehavior(log));
        });

        var publisher = provider.GetRequiredService<ParallelPublisher>();
        await publisher.Publish(new TestNotification(5));

        log.Should().Equal("before", "after");
        received.Should().ContainSingle().Which.Should().Be(5);
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class CaptureHandler(List<int> received) : INotificationHandler<TestNotification>
    {
        public ValueTask Handle(TestNotification n, CancellationToken ct)
        {
            received.Add(n.Value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : INotificationHandler<TestNotification>
    {
        public ValueTask Handle(TestNotification n, CancellationToken ct) =>
            ValueTask.FromException(new InvalidOperationException("handler failure"));
    }

    private sealed class FuncHandler(Func<TestNotification, CancellationToken, ValueTask> fn)
        : INotificationHandler<TestNotification>
    {
        public ValueTask Handle(TestNotification n, CancellationToken ct) => fn(n, ct);
    }

    private sealed class LoggingNotificationBehavior(List<string> log)
        : INotificationPipelineBehavior<TestNotification>
    {
        public async ValueTask Handle(
            TestNotification n,
            NotificationHandlerDelegate<TestNotification> next,
            CancellationToken ct)
        {
            log.Add("before");
            await next(n, ct);
            log.Add("after");
        }
    }
}
