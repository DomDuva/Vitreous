using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Vitreous.Abstractions;
using Xunit;

namespace Vitreous.SourceGenerationTests;

/// <summary>
/// Publishes through the generated <c>VitreousDispatcher</c>: a notification is published as its
/// runtime type, like a message sent through it, so one held as <see cref="INotification"/> or as
/// a base type still reaches its handlers.
/// </summary>
public class GeneratedPublishTests
{
    private const string Notifications = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Vitreous;
        using Vitreous.Abstractions;
        namespace TestProject;

        public static class Log { public static readonly List<string> Calls = new(); }

        public abstract record PubEvent : INotification;
        public sealed record PubCreated : PubEvent;
        public sealed record PubUpdated : PubEvent;
        public interface IPubAudited : INotification;
        public sealed record PubAudited : IPubAudited;
        public readonly record struct PubTick(int N) : INotification;
        public sealed record PubOrphan : INotification;

        public sealed class PubEventHandler : INotificationHandler<PubEvent>
        {
            public ValueTask Handle(PubEvent n, CancellationToken ct = default) { Log.Calls.Add("base"); return default; }
        }
        public sealed class PubCreatedHandlerA : INotificationHandler<PubCreated>
        {
            public ValueTask Handle(PubCreated n, CancellationToken ct = default) { Log.Calls.Add("created-a"); return default; }
        }
        public sealed class PubCreatedHandlerB : INotificationHandler<PubCreated>
        {
            public ValueTask Handle(PubCreated n, CancellationToken ct = default) { Log.Calls.Add("created-b"); return default; }
        }
        public sealed class PubAuditedHandler : INotificationHandler<IPubAudited>
        {
            public ValueTask Handle(IPubAudited n, CancellationToken ct = default) { Log.Calls.Add("audited"); return default; }
        }
        public sealed class PubTickHandler : INotificationHandler<PubTick>
        {
            public ValueTask Handle(PubTick n, CancellationToken ct = default) { Log.Calls.Add($"tick {n.N}"); return default; }
        }
        """;

    private static readonly Lazy<Assembly> s_assembly = new(() => GeneratorTestHelper.EmitAndLoad(Notifications));

    private static List<string> Log =>
        (List<string>)s_assembly.Value.GetType("TestProject.Log", throwOnError: true)!.GetField("Calls")!.GetValue(null)!;

    private static IPublisher CreatePublisher(NotificationPublishStrategy strategy = NotificationPublishStrategy.Sequential)
    {
        Log.Clear();
        var services = new ServiceCollection();
        var extensions = s_assembly.Value.GetType("Vitreous.VitreousGeneratedServiceCollectionExtensions", throwOnError: true)!;
        Action<VitreousOptions> configure = o => o.NotificationPublishStrategy = strategy;
        extensions.GetMethod("AddVitreousHandlers")!.Invoke(null, [services, configure]);

        var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return root.CreateScope().ServiceProvider.GetRequiredService<IPublisher>();
    }

    private static object Create(string typeName, params object[] args) =>
        Activator.CreateInstance(s_assembly.Value.GetType("TestProject." + typeName, throwOnError: true)!, args)!;

    // Publish<T> with T a type from the compiled assembly, as code written against T would call it.
    private static ValueTask PublishAs(IPublisher publisher, string typeName, object notification) =>
        (ValueTask)typeof(IPublisher).GetMethod(nameof(IPublisher.Publish))!
            .MakeGenericMethod(s_assembly.Value.GetType("TestProject." + typeName, throwOnError: true)!)
            .Invoke(publisher, [notification, CancellationToken.None])!;

    [Fact]
    public async Task Publish_HeldAsINotification_ReachesTheConcreteTypesHandlers()
    {
        var publisher = CreatePublisher();

        await publisher.Publish((INotification)Create("PubCreated"));

        Log.Should().BeEquivalentTo(["created-a", "created-b"]);
    }

    [Fact]
    public async Task Publish_DomainEventList_ReachesEveryHandler()
    {
        var publisher = CreatePublisher();
        IReadOnlyList<INotification> events = [(INotification)Create("PubCreated"), (INotification)Create("PubTick", 1)];

        foreach (var e in events)
            await publisher.Publish(e);

        Log.Should().BeEquivalentTo(["created-a", "created-b", "tick 1"]);
    }

    [Fact]
    public async Task Publish_HeldAsBaseType_UsesTheRuntimeTypesHandlers()
    {
        var publisher = CreatePublisher();

        await PublishAs(publisher, "PubEvent", Create("PubCreated"));

        Log.Should().BeEquivalentTo(["created-a", "created-b"]);
    }

    [Fact]
    public async Task Publish_RuntimeTypeWithoutHandlers_FallsBackToTheNearestBaseTypeWithHandlers()
    {
        var publisher = CreatePublisher();

        await publisher.Publish((INotification)Create("PubUpdated"));

        Log.Should().Equal("base");
    }

    [Fact]
    public async Task Publish_NoHandlersForTheRuntimeTypeOrItsBases_PublishesAsTheTypeArgument()
    {
        var publisher = CreatePublisher();

        await PublishAs(publisher, "IPubAudited", Create("PubAudited"));

        Log.Should().Equal("audited");
    }

    [Fact]
    public async Task Publish_ValueTypeHeldAsINotification_ReachesItsHandler()
    {
        var publisher = CreatePublisher();

        await publisher.Publish((INotification)Create("PubTick", 7));

        Log.Should().Equal("tick 7");
    }

    [Fact]
    public async Task Publish_ConcreteType_IsUnchanged()
    {
        var publisher = CreatePublisher();

        await PublishAs(publisher, "PubCreated", Create("PubCreated"));
        await PublishAs(publisher, "PubTick", Create("PubTick", 2));

        Log.Should().BeEquivalentTo(["created-a", "created-b", "tick 2"]);
    }

    [Fact]
    public async Task Publish_ParallelStrategy_HeldAsINotification_ReachesTheConcreteTypesHandlers()
    {
        var publisher = CreatePublisher(NotificationPublishStrategy.Parallel);

        await publisher.Publish((INotification)Create("PubCreated"));

        Log.Should().BeEquivalentTo(["created-a", "created-b"]);
    }

    [Fact]
    public async Task Publish_NoHandlersAnywhere_CompletesWithoutCallingAnything()
    {
        var publisher = CreatePublisher();

        await publisher.Publish((INotification)Create("PubOrphan"));

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
