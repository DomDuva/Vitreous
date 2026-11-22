using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Vitreous.Abstractions;
using Xunit;

namespace Vitreous.SourceGenerationTests;

/// <summary>
/// Open-generic behaviors closed by the generator for <c>AddVitreousBehavior</c>. They must run for
/// exactly the messages Microsoft.Extensions.DependencyInjection would close them over itself.
/// </summary>
public class GeneratedBehaviorTests
{
    private const string Project = """
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Vitreous;
        using Vitreous.Abstractions;
        namespace BehaviorProject;

        public sealed class Log { public List<string> Entries { get; } = new(); }

        public sealed record Item(string Name);

        public sealed record Add(int A, int B) : ICommand<int>;
        public readonly record struct Echo(string Text) : ICommand<string>;
        public sealed record Noop : ICommand;
        public sealed record Find(string Name) : IQuery<Item?>;
        public sealed record Names : IQuery<IReadOnlyList<string>>;
        public sealed record Count(int N) : IStreamQuery<int>;
        public sealed record Happened : INotification;
        public readonly record struct Ignored : INotification;

        public sealed class AddHandler : ICommandHandler<Add, int>
        { public ValueTask<Result<int>> Handle(Add c, CancellationToken ct = default) => new(c.A + c.B); }
        public sealed class EchoHandler : ICommandHandler<Echo, string>
        { public ValueTask<Result<string>> Handle(Echo c, CancellationToken ct = default) => new(c.Text); }
        public sealed class NoopHandler : ICommandHandler<Noop>
        { public ValueTask<Result> Handle(Noop c, CancellationToken ct = default) => new(Result.Success); }
        public sealed class FindHandler : IQueryHandler<Find, Item?>
        { public ValueTask<Result<Item?>> Handle(Find q, CancellationToken ct = default) => new(new Item(q.Name)); }
        public sealed class NamesHandler : IQueryHandler<Names, IReadOnlyList<string>>
        { public ValueTask<Result<IReadOnlyList<string>>> Handle(Names q, CancellationToken ct = default) => new(new[] { "a" }); }
        public sealed class CountHandler : StreamQueryHandler<Count, int>
        {
            protected override async IAsyncEnumerable<int> Stream(Count q, [EnumeratorCancellation] CancellationToken ct)
            {
                for (var i = 0; i < q.N; i++) { await Task.Yield(); yield return i; }
            }
        }
        public sealed class HappenedHandler : INotificationHandler<Happened>
        { public ValueTask Handle(Happened n, CancellationToken ct = default) => default; }

        public sealed class Every<TMessage, TResult>(Log log) : IPipelineBehavior<TMessage, TResult>
        {
            public ValueTask<Result<TResult>> Handle(TMessage m, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default)
            { log.Entries.Add("Every:" + typeof(TMessage).Name); return next(m, ct); }
        }
        public sealed class CommandsOnly<TMessage, TResult>(Log log) : IPipelineBehavior<TMessage, TResult> where TMessage : ICommand<TResult>
        {
            public ValueTask<Result<TResult>> Handle(TMessage m, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default)
            { log.Entries.Add("CommandsOnly:" + typeof(TMessage).Name); return next(m, ct); }
        }
        public sealed class ClassResults<TMessage, TResult>(Log log) : IPipelineBehavior<TMessage, TResult> where TResult : class
        {
            public ValueTask<Result<TResult>> Handle(TMessage m, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default)
            { log.Entries.Add("ClassResults:" + typeof(TMessage).Name); return next(m, ct); }
        }
        public sealed class StructMessages<TMessage, TResult>(Log log) : IPipelineBehavior<TMessage, TResult> where TMessage : struct
        {
            public ValueTask<Result<TResult>> Handle(TMessage m, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default)
            { log.Entries.Add("StructMessages:" + typeof(TMessage).Name); return next(m, ct); }
        }
        public sealed class NewableResults<TMessage, TResult>(Log log) : IPipelineBehavior<TMessage, TResult> where TMessage : notnull where TResult : new()
        {
            public ValueTask<Result<TResult>> Handle(TMessage m, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default)
            { log.Entries.Add("NewableResults:" + typeof(TMessage).Name); return next(m, ct); }
        }
        public sealed class ListResults<TMessage, TResult>(Log log) : IPipelineBehavior<TMessage, TResult> where TResult : IReadOnlyCollection<string>
        {
            public ValueTask<Result<TResult>> Handle(TMessage m, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default)
            { log.Entries.Add("ListResults:" + typeof(TMessage).Name); return next(m, ct); }
        }
        public sealed class Streams<TMessage, TResult>(Log log) : IStreamPipelineBehavior<TMessage, TResult> where TMessage : IStreamQuery<TResult>
        {
            public ValueTask<Result<IAsyncEnumerable<TResult>>> Handle(TMessage m, StreamQueryHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default)
            { log.Entries.Add("Streams:" + typeof(TMessage).Name); return next(m, ct); }
        }
        public sealed class Notifications<TNotification>(Log log) : INotificationPipelineBehavior<TNotification> where TNotification : INotification
        {
            public ValueTask Handle(TNotification n, NotificationHandlerDelegate<TNotification> next, CancellationToken ct = default)
            { log.Entries.Add("Notifications:" + typeof(TNotification).Name); return next(n, ct); }
        }
        """;

    private static readonly string[] s_behaviors =
        ["Every`2", "CommandsOnly`2", "ClassResults`2", "StructMessages`2", "NewableResults`2", "ListResults`2", "Streams`2", "Notifications`1"];

    private static readonly Lazy<Assembly> s_assembly = new(() => GeneratorTestHelper.EmitAndLoad(Project));

    private static Type ProjectType(string name) => s_assembly.Value.GetType("BehaviorProject." + name, throwOnError: true)!;

    private static object Create(string name, params object[] args) => Activator.CreateInstance(ProjectType(name), args)!;

    private static IServiceCollection Services()
    {
        var services = new ServiceCollection();
        s_assembly.Value.GetType("Vitreous.VitreousGeneratedServiceCollectionExtensions", throwOnError: true)!
            .GetMethod("AddVitreousHandlers")!.Invoke(null, [services, null]);
        services.AddSingleton(ProjectType("Log"));
        return services;
    }

    private static async Task<List<string>> DispatchEverything(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        (await sender.Send((ICommand<int>)Create("Add", 2, 3))).Value.Should().Be(5);
        (await sender.Send((ICommand<string>)Create("Echo", "hi"))).Value.Should().Be("hi");
        (await sender.Send((ICommand)Create("Noop"))).IsSuccess.Should().BeTrue();
        await SendQuery(sender, Create("Find", "x"));
        await SendQuery(sender, Create("Names"));
        await foreach (var _ in (await sender.CreateStream((IStreamQuery<int>)Create("Count", 2))).Value) { }
        await Publish(publisher, Create("Happened"));
        await Publish(publisher, Create("Ignored"));

        var log = (System.Collections.IEnumerable)ProjectType("Log").GetProperty("Entries")!.GetValue(scope.ServiceProvider.GetRequiredService(ProjectType("Log")))!;
        return log.Cast<string>().ToList();
    }

    // The query and notification types are only known at runtime here, so call the generic overloads by reflection.
    private static async Task SendQuery(ISender sender, object query)
    {
        var resultType = query.GetType().GetInterfaces().Single(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>)).GetGenericArguments()[0];
        var send = typeof(ISender).GetMethods().Single(static m => m.Name == "Send" && m.IsGenericMethod &&
            m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IQuery<>)).MakeGenericMethod(resultType);
        var task = send.Invoke(sender, [query, CancellationToken.None])!;
        await (Task)task.GetType().GetMethod("AsTask")!.Invoke(task, null)!;
    }

    private static async Task Publish(IPublisher publisher, object notification)
    {
        var publish = typeof(IPublisher).GetMethod("Publish")!.MakeGenericMethod(notification.GetType());
        await ((ValueTask)publish.Invoke(publisher, [notification, CancellationToken.None])!).AsTask();
    }

    [Fact]
    public async Task ClosedBehaviors_RunForTheSameMessagesAsOpenGenericsInDI()
    {
        // What Microsoft.Extensions.DependencyInjection does with open generics (JIT only).
        var open = Services();
        foreach (var name in s_behaviors)
        {
            var behavior = ProjectType(name);
            var service = behavior.GetInterfaces().Single(static i => i.IsGenericType && i.Namespace == "Vitreous.Abstractions").GetGenericTypeDefinition();
            open.Add(new ServiceDescriptor(service, behavior, ServiceLifetime.Transient));
        }

        var closed = Services();
        foreach (var name in s_behaviors)
            closed.AddVitreousBehavior(ProjectType(name));

        closed.Should().NotContain(static d => d.ServiceType.IsGenericTypeDefinition,
            "every behavior is registered closed, so Native AOT never has to close one");

        var expected = await DispatchEverything(open);
        expected.Should().Contain(["Every:Add", "Every:Echo", "Every:Noop", "StructMessages:Echo", "Streams:Count", "Notifications:Happened"]);
        (await DispatchEverything(closed)).Should().Equal(expected);
    }

    [Fact]
    public void ABehaviorWithAVariantInterfaceConstraint_IsLeftToDI()
    {
        const string source = Project + """

            public sealed class Variant<TMessage, TResult> : IPipelineBehavior<TMessage, TResult> where TResult : IEnumerable<object>
            {
                public ValueTask<Result<TResult>> Handle(TMessage m, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default) => next(m, ct);
            }
            """;

        var extensions = GeneratedExtensions(source);

        extensions.Should().Contain("typeof(global::BehaviorProject.Every<,>)")
            .And.NotContain("Variant<");
    }

    [Fact]
    public void ABehaviorInAReferencedProject_IsClosed()
    {
        var library = GeneratorTestHelper.EmitAsReference("""
            using System.Threading;
            using System.Threading.Tasks;
            using Vitreous;
            using Vitreous.Abstractions;
            namespace BehaviorLibrary;

            public sealed class Audit<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
            {
                public ValueTask<Result<TResult>> Handle(TMessage m, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default) => next(m, ct);
            }

            internal sealed class Hidden<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
            {
                public ValueTask<Result<TResult>> Handle(TMessage m, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default) => next(m, ct);
            }
            """, "BehaviorLibrary");

        var (compilation, errors) = GeneratorTestHelper.CompileWithGenerator(Project, "BehaviorApp", library);

        errors.Should().BeEmpty();
        var extensions = compilation.SyntaxTrees.Single(static t => t.FilePath.EndsWith("VitreousServiceCollectionExtensions.g.cs", StringComparison.Ordinal)).ToString();
        extensions.Should().Contain("new ServiceDescriptor(typeof(IPipelineBehavior<global::BehaviorProject.Noop, global::Vitreous.Unit>), typeof(global::BehaviorLibrary.Audit<global::BehaviorProject.Noop, global::Vitreous.Unit>), lifetime)")
            .And.NotContain("Hidden<");
    }

    [Fact]
    public void GeneratedRegistrations_CompileWithoutWarnings()
    {
        var (compilation, errors) = GeneratorTestHelper.CompileWithGenerator(Project);

        errors.Should().BeEmpty();
        compilation.GetDiagnostics()
            .Where(static d => d.Severity >= DiagnosticSeverity.Warning && d.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true)
            .Should().BeEmpty();
    }

    [Fact]
    public void VitreousWithoutGeneratedBehaviors_GetsNoClosedBehaviors_AndCompiles()
    {
        var path = typeof(GeneratedBehaviorTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static a => a.Key == "Vitreous030").Value!;

        var (sources, errors) = GeneratorTestHelper.CompileAgainstVitreous(Project, MetadataReference.CreateFromFile(path));

        errors.Should().BeEmpty();
        sources.Single(static s => s.HintName == "VitreousServiceCollectionExtensions.g.cs").SourceText.ToString()
            .Should().NotContain("AddVitreousGeneratedBehaviors");
    }

    private static string GeneratedExtensions(string source) =>
        GeneratorTestHelper.RunGenerator(source).GeneratedSources
            .Single(static s => s.HintName == "VitreousServiceCollectionExtensions.g.cs").SourceText.ToString();
}
