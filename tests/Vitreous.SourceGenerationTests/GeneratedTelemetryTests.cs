using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Vitreous.Abstractions;
using Xunit;

namespace Vitreous.SourceGenerationTests;

/// <summary>
/// OpenTelemetry spans around dispatch through the generated <c>VitreousDispatcher</c>, and the
/// generator's fallback to plain dispatch when the referenced Vitreous predates telemetry.
/// </summary>
public class GeneratedTelemetryTests
{
    // A namespace of its own: spans are matched by message type, and other tests dispatch in parallel.
    private const string Messages = """
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Vitreous;
        using Vitreous.Abstractions;
        namespace TelemetryProject;

        public sealed record Add(int A, int B) : ICommand<int>;
        public sealed class AddHandler : ICommandHandler<Add, int>
        {
            public ValueTask<Result<int>> Handle(Add c, CancellationToken ct = default) => new(c.A + c.B);
        }

        public sealed record Noop : ICommand;
        public sealed class NoopHandler : ICommandHandler<Noop>
        {
            public ValueTask<Result> Handle(Noop c, CancellationToken ct = default) => new(Result.Success);
        }

        public sealed record Missing : IQuery<string>;
        public sealed class MissingHandler : IQueryHandler<Missing, string>
        {
            public ValueTask<Result<string>> Handle(Missing q, CancellationToken ct = default) => new(Error.NotFound("Nothing here."));
        }

        public sealed record Count(int N) : IStreamQuery<int>;
        public sealed class CountHandler : StreamQueryHandler<Count, int>
        {
            protected override async IAsyncEnumerable<int> Stream(Count q, [EnumeratorCancellation] CancellationToken ct)
            {
                for (var i = 1; i <= q.N; i++) { await Task.Yield(); yield return i; }
            }
        }
        """;

    private static readonly Lazy<Assembly> s_assembly = new(() => GeneratorTestHelper.EmitAndLoad(Messages));

    private static ISender CreateSender()
    {
        var services = new ServiceCollection();
        var extensions = s_assembly.Value.GetType("Vitreous.VitreousGeneratedServiceCollectionExtensions", throwOnError: true)!;
        extensions.GetMethod("AddVitreousHandlers")!.Invoke(null, [services, null]);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ISender>();
    }

    private static T Create<T>(string typeName, params object[] args) =>
        (T)Activator.CreateInstance(s_assembly.Value.GetType("TelemetryProject." + typeName, throwOnError: true)!, args)!;

    [Fact]
    public void Dispatcher_CallsDispatchTelemetry_WhenVitreousHasIt()
    {
        var (_, sources) = GeneratorTestHelper.RunGenerator(Messages);

        var dispatcher = sources.Single(static s => s.HintName == "VitreousDispatcher.g.cs").SourceText.ToString();
        dispatcher.Should().Contain("DispatchTelemetry.IsEnabled")
            .And.Contain("DispatchTelemetry.SendCommand(invoker, _services, command, ct)")
            .And.Contain("DispatchTelemetry.SendQuery(invoker, _services, query, ct)")
            .And.Contain("DispatchTelemetry.CreateStream(invoker, _services, query, ct)");
    }

    [Fact]
    public async Task GeneratedDispatcher_RecordsASpanPerDispatch()
    {
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo  = static s => s.Name == VitreousTelemetry.ActivitySourceName,
            Sample          = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        var sender = CreateSender();

        (await sender.Send(Create<ICommand<int>>("Add", 2, 3))).Value.Should().Be(5);
        (await sender.Send(Create<ICommand>("Noop"))).IsSuccess.Should().BeTrue();
        (await sender.Send(Create<IQuery<string>>("Missing"))).IsFailure.Should().BeTrue();
        await foreach (var _ in (await sender.CreateStream(Create<IStreamQuery<int>>("Count", 2))).Value) { }

        var spans = stopped
            .Where(static a => ((string?)a.GetTagItem("vitreous.message.type"))?.StartsWith("TelemetryProject.", StringComparison.Ordinal) == true)
            .ToDictionary(static a => a.DisplayName);

        spans.Keys.Should().BeEquivalentTo(
            "vitreous command Add", "vitreous command Noop", "vitreous query Missing", "vitreous stream Count");
        spans["vitreous query Missing"].Status.Should().Be(ActivityStatusCode.Error);
        spans["vitreous query Missing"].GetTagItem("error.type").Should().Be("NOT_FOUND");
        spans["vitreous stream Count"].GetTagItem("vitreous.stream.items").Should().Be(2L);
    }

    [Fact]
    public void VitreousWithoutDispatchTelemetry_GetsThePlainDispatcher_AndCompiles()
    {
        // The generator may be upgraded without Vitreous; 0.3.0 has no DispatchTelemetry.
        var path = typeof(GeneratedTelemetryTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static a => a.Key == "Vitreous030").Value!;
        File.Exists(path).Should().BeTrue($"the Vitreous 0.3.0 package is downloaded on restore ({path})");

        var (sources, errors) = GeneratorTestHelper.CompileAgainstVitreous(Messages, MetadataReference.CreateFromFile(path));

        errors.Should().BeEmpty();
        var dispatcher = sources.Single(static s => s.HintName == "VitreousDispatcher.g.cs").SourceText.ToString();
        dispatcher.Should().NotContain("DispatchTelemetry");
        dispatcher.Should().Contain("return invoker(_services, command, ct);");
    }
}
