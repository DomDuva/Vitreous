using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Vitreous.Abstractions;
using Xunit;

namespace Vitreous.SourceGenerationTests;

/// <summary>
/// Compiles sources with the generator, loads the result, and dispatches through the
/// generated <c>VitreousDispatcher</c> to verify runtime behavior (not just that it compiles).
/// </summary>
public class GeneratedDispatcherTests
{
    private const string Messages = """
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Vitreous;
        using Vitreous.Abstractions;
        namespace TestProject;

        public record BaseQ : IQuery<string>;
        public sealed record DerivedWithOwnHandlerQ : BaseQ;
        public sealed record DerivedWithoutHandlerQ : BaseQ;

        public sealed class BaseQHandler : IQueryHandler<BaseQ, string>
        {
            public ValueTask<Result<string>> Handle(BaseQ q, CancellationToken ct = default) => new("base");
        }

        public sealed class DerivedQHandler : IQueryHandler<DerivedWithOwnHandlerQ, string>
        {
            public ValueTask<Result<string>> Handle(DerivedWithOwnHandlerQ q, CancellationToken ct = default) => new("derived");
        }

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

        public sealed record Count(int N) : IStreamQuery<int>;
        public sealed class CountHandler : StreamQueryHandler<Count, int>
        {
            protected override async IAsyncEnumerable<int> Stream(Count q, [EnumeratorCancellation] CancellationToken ct)
            {
                for (var i = 1; i <= q.N; i++) { await Task.Yield(); yield return i; }
            }
        }

        public sealed record Unhandled : IQuery<int>;
        """;

    private static readonly Lazy<Assembly> s_assembly = new(() => GeneratorTestHelper.EmitAndLoad(Messages));

    private static ISender CreateSender()
    {
        var services = new ServiceCollection();
        var extensions = s_assembly.Value.GetType("Vitreous.VitreousGeneratedServiceCollectionExtensions", throwOnError: true)!;
        extensions.GetMethod("AddVitreousHandlers")!.Invoke(null, [services, null]);

        var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return root.CreateScope().ServiceProvider.GetRequiredService<ISender>();
    }

    private static T Create<T>(string typeName, params object[] args) =>
        (T)Activator.CreateInstance(s_assembly.Value.GetType("TestProject." + typeName, throwOnError: true)!, args)!;

    [Fact]
    public void GeneratedTypes_AreInternal()
    {
        s_assembly.Value.GetType("Vitreous.VitreousDispatcher")!.IsPublic.Should().BeFalse();
        s_assembly.Value.GetType("Vitreous.VitreousGeneratedServiceCollectionExtensions")!.IsPublic.Should().BeFalse();
    }

    [Fact]
    public async Task Send_DerivedMessageWithItsOwnHandler_UsesItsOwnHandler()
    {
        var result = await CreateSender().Send(Create<IQuery<string>>("DerivedWithOwnHandlerQ"));

        result.Value.Should().Be("derived");
    }

    [Fact]
    public async Task Send_DerivedMessageWithoutAHandler_FallsBackToTheBaseHandler()
    {
        var sender = CreateSender();

        (await sender.Send(Create<IQuery<string>>("DerivedWithoutHandlerQ"))).Value.Should().Be("base");
        (await sender.Send(Create<IQuery<string>>("BaseQ"))).Value.Should().Be("base");
    }

    [Fact]
    public async Task Send_CommandVoidCommandAndStream_Dispatch()
    {
        var sender = CreateSender();

        (await sender.Send(Create<ICommand<int>>("Add", 2, 3))).Value.Should().Be(5);
        (await sender.Send(Create<ICommand>("Noop"))).IsSuccess.Should().BeTrue();

        var stream = await sender.CreateStream(Create<IStreamQuery<int>>("Count", 3));
        var items = new List<int>();
        await foreach (var item in stream.Value)
            items.Add(item);
        items.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Send_MessageWithNoHandler_ThrowsWithMessageName()
    {
        var act = async () => await CreateSender().Send(Create<IQuery<int>>("Unhandled"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Unhandled*");
    }

    [Fact]
    public void ReferencedProjectThatAlsoRunsTheGenerator_DoesNotConflict()
    {
        // App runs the generator; Tests references App and runs the generator too.
        // Public generated types used to collide here (CS0121 / CS0436).
        const string app = """
            using System.Threading;
            using System.Threading.Tasks;
            using Vitreous;
            using Vitreous.Abstractions;
            namespace App;

            public sealed record Q : IQuery<int>;
            public sealed class QH : IQueryHandler<Q, int>
            {
                public ValueTask<Result<int>> Handle(Q q, CancellationToken ct = default) => new(1);
            }
            """;
        const string tests = """
            using Microsoft.Extensions.DependencyInjection;
            using Vitreous;
            namespace Tests;

            public static class Setup
            {
                public static void Configure(IServiceCollection services) => services.AddVitreousHandlers();
            }
            """;

        var appReference = GeneratorTestHelper.EmitAsReference(app, "App");
        var (_, errors) = GeneratorTestHelper.CompileWithGenerator(tests, "Tests", appReference);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void VIT001_DerivedMessageWhoseBaseHasAHandler_IsNotReported()
    {
        var (diagnostics, _) = GeneratorTestHelper.RunGenerator(Messages);

        diagnostics.Where(static d => d.Id == "VIT001")
            .Select(static d => d.GetMessage())
            .Should().ContainSingle()
            .Which.Should().Contain("Unhandled");
    }
}
