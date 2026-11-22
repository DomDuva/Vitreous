namespace Vitreous.UnitTests.Registration;

/// <summary>
/// Covers <c>ReflectionPublisher</c>, the <see cref="IPublisher"/> of the reflection-based path:
/// notifications are published as their runtime type, like messages sent through
/// <c>ReflectionSender</c>, so one held as <see cref="INotification"/> or as a base type still
/// reaches its handlers.
/// </summary>
public sealed class ReflectionPublisherTests
{
    // ── Fixture notifications / handlers (discovered by scanning this assembly) ──

    public abstract record PubEvent : INotification;
    public sealed record PubCreated : PubEvent;      // has handlers of its own
    public sealed record PubUpdated : PubEvent;      // no handlers: falls back to PubEvent's
    public interface IPubAudited : INotification;
    public sealed record PubAudited : IPubAudited;   // handled only through the interface
    public readonly record struct PubTick(int N) : INotification;
    public sealed record PubOrphan : INotification;  // no handlers anywhere

    private static readonly List<string> Log = [];

    public sealed class PubEventHandler : INotificationHandler<PubEvent>
    {
        public ValueTask Handle(PubEvent notification, CancellationToken ct = default) { Log.Add("base"); return default; }
    }

    public sealed class PubCreatedHandlerA : INotificationHandler<PubCreated>
    {
        public ValueTask Handle(PubCreated notification, CancellationToken ct = default) { Log.Add("created-a"); return default; }
    }

    public sealed class PubCreatedHandlerB : INotificationHandler<PubCreated>
    {
        public ValueTask Handle(PubCreated notification, CancellationToken ct = default) { Log.Add("created-b"); return default; }
    }

    public sealed class PubAuditedHandler : INotificationHandler<IPubAudited>
    {
        public ValueTask Handle(IPubAudited notification, CancellationToken ct = default) { Log.Add("audited"); return default; }
    }

    public sealed class PubTickHandler : INotificationHandler<PubTick>
    {
        public ValueTask Handle(PubTick notification, CancellationToken ct = default) { Log.Add($"tick {notification.N}"); return default; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IPublisher CreatePublisher(NotificationPublishStrategy strategy = NotificationPublishStrategy.Sequential)
    {
        Log.Clear();
        var services = new ServiceCollection();
        services.AddVitreousHandlers(
            o => o.NotificationPublishStrategy = strategy,
            typeof(ReflectionPublisherTests).Assembly);
        var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return root.CreateScope().ServiceProvider.GetRequiredService<IPublisher>();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_HeldAsINotification_ReachesTheConcreteTypesHandlers()
    {
        var publisher = CreatePublisher();
        INotification notification = new PubCreated();

        await publisher.Publish(notification);

        Log.Should().BeEquivalentTo(["created-a", "created-b"]);
    }

    [Fact]
    public async Task Publish_DomainEventList_ReachesEveryHandler()
    {
        var publisher = CreatePublisher();
        IReadOnlyList<INotification> events = [new PubCreated(), new PubTick(1)];

        foreach (var e in events)
            await publisher.Publish(e);

        Log.Should().BeEquivalentTo(["created-a", "created-b", "tick 1"]);
    }

    [Fact]
    public async Task Publish_HeldAsBaseType_UsesTheRuntimeTypesHandlers()
    {
        var publisher = CreatePublisher();
        PubEvent notification = new PubCreated();

        await publisher.Publish(notification);

        Log.Should().BeEquivalentTo(["created-a", "created-b"]);
    }

    [Fact]
    public async Task Publish_RuntimeTypeWithoutHandlers_FallsBackToTheNearestBaseTypeWithHandlers()
    {
        var publisher = CreatePublisher();
        INotification notification = new PubUpdated();

        await publisher.Publish(notification);

        Log.Should().Equal("base");
    }

    [Fact]
    public async Task Publish_NoHandlersForTheRuntimeTypeOrItsBases_PublishesAsTheTypeArgument()
    {
        var publisher = CreatePublisher();
        IPubAudited notification = new PubAudited();

        await publisher.Publish(notification);

        Log.Should().Equal("audited");
    }

    [Fact]
    public async Task Publish_ValueTypeHeldAsINotification_ReachesItsHandler()
    {
        var publisher = CreatePublisher();
        INotification notification = new PubTick(7);

        await publisher.Publish(notification);

        Log.Should().Equal("tick 7");
    }

    [Fact]
    public async Task Publish_ConcreteType_IsUnchanged()
    {
        var publisher = CreatePublisher();

        await publisher.Publish(new PubCreated());
        await publisher.Publish(new PubTick(2));

        Log.Should().BeEquivalentTo(["created-a", "created-b", "tick 2"]);
    }

    [Fact]
    public async Task Publish_ParallelStrategy_HeldAsINotification_ReachesTheConcreteTypesHandlers()
    {
        var publisher = CreatePublisher(NotificationPublishStrategy.Parallel);
        INotification notification = new PubCreated();

        await publisher.Publish(notification);

        Log.Should().BeEquivalentTo(["created-a", "created-b"]);
    }

    [Fact]
    public async Task Publish_NoHandlersAnywhere_CompletesWithoutCallingAnything()
    {
        var publisher = CreatePublisher();
        INotification notification = new PubOrphan();

        await publisher.Publish(notification);

        Log.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_Null_Throws()
    {
        var publisher = CreatePublisher();

        var act = async () => await publisher.Publish<INotification>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("notification");
    }
}
