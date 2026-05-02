namespace Vitreous.UnitTests.Pipeline;

public sealed class SequentialPublisherTests
{
    private sealed record TestNotification(int Value) : INotification;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<SequentialPublisher>();
        configure(services);
        return services.BuildServiceProvider();
    }

    // ── Fan-out correctness ───────────────────────────────────────────────────

    [Fact]
    public async Task Publish_ZeroHandlers_CompletesWithoutError()
    {
        var provider = BuildProvider(_ => { });
        var publisher = provider.GetRequiredService<SequentialPublisher>();

        var act = () => publisher.Publish(new TestNotification(1)).AsTask();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Publish_SingleHandler_HandlerReceivesNotification()
    {
        var received = new List<int>();
        var provider = BuildProvider(s =>
            s.AddSingleton<INotificationHandler<TestNotification>>(new CaptureHandler(received)));

        var publisher = provider.GetRequiredService<SequentialPublisher>();
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

        var publisher = provider.GetRequiredService<SequentialPublisher>();
        await publisher.Publish(new TestNotification(7));

        received.Should().HaveCount(3);
        received.Should().AllSatisfy(v => v.Should().Be(7));
    }

    [Fact]
    public async Task Publish_MultipleHandlers_InvokedInRegistrationOrder()
    {
        var order = new List<string>();
        var provider = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(
                new OrderedCaptureHandler(order, "first"));
            s.AddSingleton<INotificationHandler<TestNotification>>(
                new OrderedCaptureHandler(order, "second"));
        });

        var publisher = provider.GetRequiredService<SequentialPublisher>();
        await publisher.Publish(new TestNotification(0));

        order.Should().Equal("first", "second");
    }

    // ── Fail-fast behaviour ───────────────────────────────────────────────────

    [Fact]
    public async Task Publish_FirstHandlerThrows_ExceptionPropagates()
    {
        var provider = BuildProvider(s =>
            s.AddSingleton<INotificationHandler<TestNotification>>(new ThrowingHandler()));

        var publisher = provider.GetRequiredService<SequentialPublisher>();
        var act = () => publisher.Publish(new TestNotification(1)).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("handler failure");
    }

    [Fact]
    public async Task Publish_FirstHandlerThrows_SubsequentHandlerNotCalled()
    {
        var received = new List<int>();
        var provider = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(new ThrowingHandler());
            s.AddSingleton<INotificationHandler<TestNotification>>(new CaptureHandler(received));
        });

        var publisher = provider.GetRequiredService<SequentialPublisher>();
        try { await publisher.Publish(new TestNotification(1)); } catch { /* expected */ }

        received.Should().BeEmpty();
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

        var publisher = provider.GetRequiredService<SequentialPublisher>();
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

    private sealed class OrderedCaptureHandler(List<string> order, string name)
        : INotificationHandler<TestNotification>
    {
        public ValueTask Handle(TestNotification n, CancellationToken ct)
        {
            order.Add(name);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : INotificationHandler<TestNotification>
    {
        public ValueTask Handle(TestNotification n, CancellationToken ct) =>
            throw new InvalidOperationException("handler failure");
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
