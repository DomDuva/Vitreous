using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Vitreous.SourceGenerationTests;

public class VitreousGeneratorTests
{
    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public void HappyPath_CommandWithHandler_GeneratesDispatcherAndNoWarnings()
    {
        const string source = """
            using Vitreous.Abstractions;
            namespace TestProject;

            public record MyResult;
            public class MyCommand : ICommand<MyResult> { }
            public class MyCommandHandler : ICommandHandler<MyCommand, MyResult>
            {
                public System.Threading.Tasks.ValueTask<Vitreous.Result<MyResult>> Handle(
                    MyCommand command, System.Threading.CancellationToken ct = default) => default;
            }
            """;

        var (diagnostics, sources) = GeneratorTestHelper.RunGenerator(source);

        // No VIT001 / VIT002 diagnostics
        diagnostics.Where(static d => d.Id is "VIT001" or "VIT002").Should().BeEmpty();

        // Both source files emitted
        sources.Should().HaveCount(2);
        sources.Select(static s => s.HintName)
            .Should().Contain("VitreousServiceCollectionExtensions.g.cs")
            .And.Contain("VitreousDispatcher.g.cs");

        // Dispatcher contains a branch for the registered command
        var dispatcher = sources.First(static s => s.HintName == "VitreousDispatcher.g.cs")
            .SourceText.ToString();
        dispatcher.Should().Contain("TestProject.MyCommand");
    }

    [Fact]
    public void HappyPath_QueryWithHandler_GeneratesDispatcherAndNoWarnings()
    {
        const string source = """
            using Vitreous.Abstractions;
            namespace TestProject;

            public record ListResult;
            public class ListQuery : IQuery<ListResult> { }
            public class ListQueryHandler : IQueryHandler<ListQuery, ListResult>
            {
                public System.Threading.Tasks.ValueTask<Vitreous.Result<ListResult>> Handle(
                    ListQuery query, System.Threading.CancellationToken ct = default) => default;
            }
            """;

        var (diagnostics, sources) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Where(static d => d.Id is "VIT001" or "VIT002").Should().BeEmpty();

        var dispatcher = sources.First(static s => s.HintName == "VitreousDispatcher.g.cs")
            .SourceText.ToString();
        dispatcher.Should().Contain("TestProject.ListQuery");
    }

    [Fact]
    public void HappyPath_StreamQueryWithHandler_GeneratesCreateStreamAndNoWarnings()
    {
        const string source = """
            using Vitreous.Abstractions;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            namespace TestProject;

            public record OrderDto(string Name);
            public class StreamOrdersQuery : IStreamQuery<OrderDto> { }
            public class StreamOrdersHandler : IStreamQueryHandler<StreamOrdersQuery, OrderDto>
            {
                public System.Threading.Tasks.ValueTask<Vitreous.Result<IAsyncEnumerable<OrderDto>>> Handle(
                    StreamOrdersQuery query, System.Threading.CancellationToken ct = default) => default;
            }
            """;

        var (diagnostics, sources) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Where(static d => d.Id is "VIT001" or "VIT002" or "VIT003").Should().BeEmpty();

        var dispatcher = sources.First(static s => s.HintName == "VitreousDispatcher.g.cs")
            .SourceText.ToString();

        // CreateStream method and type branch emitted
        dispatcher.Should().Contain("CreateStream");
        dispatcher.Should().Contain("TestProject.StreamOrdersQuery");

        // Handler registered in the service collection extensions
        var registrations = sources.First(static s => s.HintName == "VitreousServiceCollectionExtensions.g.cs")
            .SourceText.ToString();
        registrations.Should().Contain("IStreamQueryHandler<TestProject.StreamOrdersQuery, TestProject.OrderDto>");
    }

    // -------------------------------------------------------------------------
    // VIT001 — no handler found
    // -------------------------------------------------------------------------

    [Fact]
    public void VIT001_CommandWithNoHandler_EmitsWarning()
    {
        const string source = """
            using Vitreous.Abstractions;
            namespace TestProject;

            public record MyResult;
            public class OrphanCommand : ICommand<MyResult> { }
            """;

        var (diagnostics, _) = GeneratorTestHelper.RunGenerator(source);

        var vit001 = diagnostics.Where(static d => d.Id == "VIT001").ToList();
        vit001.Should().HaveCount(1);
        vit001[0].Severity.Should().Be(DiagnosticSeverity.Warning);
        vit001[0].GetMessage().Should().Contain("OrphanCommand");
        vit001[0].GetMessage().Should().Contain("Command");
    }

    [Fact]
    public void VIT001_QueryWithNoHandler_EmitsWarning()
    {
        const string source = """
            using Vitreous.Abstractions;
            namespace TestProject;

            public record QueryResult;
            public class OrphanQuery : IQuery<QueryResult> { }
            """;

        var (diagnostics, _) = GeneratorTestHelper.RunGenerator(source);

        var vit001 = diagnostics.Where(static d => d.Id == "VIT001").ToList();
        vit001.Should().HaveCount(1);
        vit001[0].Severity.Should().Be(DiagnosticSeverity.Warning);
        vit001[0].GetMessage().Should().Contain("OrphanQuery");
        vit001[0].GetMessage().Should().Contain("Query");
    }

    [Fact]
    public void VIT001_StreamQueryWithNoHandler_EmitsWarning()
    {
        const string source = """
            using Vitreous.Abstractions;
            namespace TestProject;

            public record ItemDto;
            public class OrphanStreamQuery : IStreamQuery<ItemDto> { }
            """;

        var (diagnostics, _) = GeneratorTestHelper.RunGenerator(source);

        var vit001 = diagnostics.Where(static d => d.Id == "VIT001").ToList();
        vit001.Should().HaveCount(1);
        vit001[0].Severity.Should().Be(DiagnosticSeverity.Warning);
        vit001[0].GetMessage().Should().Contain("OrphanStreamQuery");
        vit001[0].GetMessage().Should().Contain("StreamQuery");
    }

    [Fact]
    public void VIT001_Notification_WithNoHandler_IsNotEmitted()
    {
        // Notifications are allowed to have zero handlers — VIT001 must not fire.
        const string source = """
            using Vitreous.Abstractions;
            namespace TestProject;

            public class OrderCreated : INotification { }
            """;

        var (diagnostics, _) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Where(static d => d.Id == "VIT001").Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // VIT002 — multiple handlers
    // -------------------------------------------------------------------------

    [Fact]
    public void VIT002_CommandWithTwoHandlers_EmitsError()
    {
        const string source = """
            using Vitreous.Abstractions;
            namespace TestProject;

            public class VoidCmd : ICommand { }
            public class H1 : ICommandHandler<VoidCmd>
            {
                public System.Threading.Tasks.ValueTask<Vitreous.Result> Handle(
                    VoidCmd cmd, System.Threading.CancellationToken ct = default) => default;
            }
            public class H2 : ICommandHandler<VoidCmd>
            {
                public System.Threading.Tasks.ValueTask<Vitreous.Result> Handle(
                    VoidCmd cmd, System.Threading.CancellationToken ct = default) => default;
            }
            """;

        var (diagnostics, _) = GeneratorTestHelper.RunGenerator(source);

        var vit002 = diagnostics.Where(static d => d.Id == "VIT002").ToList();
        vit002.Should().HaveCount(1);
        vit002[0].Severity.Should().Be(DiagnosticSeverity.Error);
        vit002[0].GetMessage().Should().Contain("TestProject.VoidCmd");
    }

    // -------------------------------------------------------------------------
    // VIT003 — missing [EnumeratorCancellation]
    // -------------------------------------------------------------------------

    [Fact]
    public void VIT003_StreamHandler_MissingEnumeratorCancellation_EmitsWarning()
    {
        const string source = """
            using Vitreous.Abstractions;
            using System.Collections.Generic;
            using System.Threading;
            namespace TestProject;

            public record ItemDto;
            public class MyStreamQuery : IStreamQuery<ItemDto> { }
            public class MyStreamHandler : IStreamQueryHandler<MyStreamQuery, ItemDto>
            {
                public System.Threading.Tasks.ValueTask<Vitreous.Result<IAsyncEnumerable<ItemDto>>> Handle(
                    MyStreamQuery query, System.Threading.CancellationToken ct = default) => default;

                // Missing [EnumeratorCancellation] on ct
                private async IAsyncEnumerable<ItemDto> Stream(CancellationToken ct)
                {
                    yield return new ItemDto();
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var (diagnostics, _) = GeneratorTestHelper.RunGenerator(source);

        var vit003 = diagnostics.Where(static d => d.Id == "VIT003").ToList();
        vit003.Should().HaveCount(1);
        vit003[0].Severity.Should().Be(DiagnosticSeverity.Warning);
        vit003[0].GetMessage().Should().Contain("Stream");
        vit003[0].GetMessage().Should().Contain("MyStreamHandler");
    }

    [Fact]
    public void VIT003_StreamHandler_WithEnumeratorCancellation_NoWarning()
    {
        const string source = """
            using Vitreous.Abstractions;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            namespace TestProject;

            public record ItemDto;
            public class MyStreamQuery : IStreamQuery<ItemDto> { }
            public class MyStreamHandler : IStreamQueryHandler<MyStreamQuery, ItemDto>
            {
                public System.Threading.Tasks.ValueTask<Vitreous.Result<IAsyncEnumerable<ItemDto>>> Handle(
                    MyStreamQuery query, System.Threading.CancellationToken ct = default) => default;

                private async IAsyncEnumerable<ItemDto> Stream([EnumeratorCancellation] CancellationToken ct)
                {
                    yield return new ItemDto();
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var (diagnostics, _) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Where(static d => d.Id == "VIT003").Should().BeEmpty();
    }
}
