using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Vitreous.SourceGenerationTests;

/// <summary>
/// Generated code compiles with the consumer's language version, not Vitreous's. It must stay
/// warning-free down to the oldest version a net8.0 project can use. Newer versions than Roslyn
/// 4.8 knows about are covered by the package smoke test, which builds on every supported SDK.
/// </summary>
public class GeneratedCodeLanguageVersionTests
{
    private const string Messages = """
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Vitreous;
        using Vitreous.Abstractions;
        namespace TestProject;

        public sealed record Add(int A, int B) : ICommand<int>;
        public sealed class AddHandler : ICommandHandler<Add, int>
        {
            public ValueTask<Result<int>> Handle(Add c, CancellationToken ct = default) => new(c.A + c.B);
        }

        public readonly record struct Rename(string Name) : ICommand;
        public sealed class RenameHandler : ICommandHandler<Rename>
        {
            public ValueTask<Result> Handle(Rename c, CancellationToken ct = default) => new(Result.Success);
        }

        public sealed record GetNames(int Count) : IQuery<IReadOnlyList<string?>>;
        public sealed class GetNamesHandler : IQueryHandler<GetNames, IReadOnlyList<string?>>
        {
            public ValueTask<Result<IReadOnlyList<string?>>> Handle(GetNames q, CancellationToken ct = default) =>
                new(new string?[q.Count]);
        }

        public sealed record Count(int N) : IStreamQuery<int>;
        public sealed class CountHandler : StreamQueryHandler<Count, int>
        {
            protected override async IAsyncEnumerable<int> Stream(Count q, [EnumeratorCancellation] CancellationToken ct)
            {
                for (var i = 1; i <= q.N; i++) { await Task.Yield(); yield return i; }
            }
        }

        public sealed record Renamed(string Name) : INotification;
        public sealed class AuditRenamed : INotificationHandler<Renamed>
        {
            public ValueTask Handle(Renamed n, CancellationToken ct = default) => default;
        }
        public sealed class IndexRenamed : INotificationHandler<Renamed>
        {
            public ValueTask Handle(Renamed n, CancellationToken ct = default) => default;
        }
        """;

    [Theory]
    [InlineData(LanguageVersion.CSharp12)] // the net8.0 default
    [InlineData(LanguageVersion.Latest)]   // the newest version Roslyn 4.8 knows
    public void Generated_code_compiles_without_warnings(LanguageVersion languageVersion)
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        var compilation = GeneratorTestHelper.CreateCompilation(Messages)
            .WithOptions(new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        compilation = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(Messages, parseOptions));

        var driver = CSharpGeneratorDriver.Create(
            [new Vitreous.SourceGeneration.VitreousGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);

        updated.SyntaxTrees.Should().HaveCountGreaterThan(1, "the generator must emit source");
        generatorDiagnostics.Concat(updated.GetDiagnostics())
            .Where(static d => d.Severity >= DiagnosticSeverity.Warning)
            .Should().BeEmpty();
    }
}
