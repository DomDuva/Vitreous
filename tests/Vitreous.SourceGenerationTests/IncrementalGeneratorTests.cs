using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Vitreous.SourceGenerationTests;

/// <summary>
/// Verifies that the generator's pipeline is actually incremental: edits that cannot affect the
/// output must not regenerate sources.
/// </summary>
public class IncrementalGeneratorTests
{
    private const string Handlers = """
        using System.Threading;
        using System.Threading.Tasks;
        using Vitreous;
        using Vitreous.Abstractions;
        namespace TestProject;

        public sealed record Q : IQuery<int>;
        public sealed class QH : IQueryHandler<Q, int>
        {
            public ValueTask<Result<int>> Handle(Q q, CancellationToken ct = default) => new(1);
        }
        """;

    private const string Unrelated = """
        namespace TestProject;

        public static class Helpers
        {
            public static int Answer() => 41;
        }
        """;

    private static GeneratorDriver CreateDriver() =>
        CSharpGeneratorDriver.Create(
            [new Vitreous.SourceGeneration.VitreousGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static Compilation Edit(Compilation compilation, int fileIndex, string find, string replace)
    {
        var tree = compilation.SyntaxTrees.Single(t => t.FilePath == $"File{fileIndex}.cs");
        var text = tree.GetText();
        var start = text.ToString().IndexOf(find, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        var changed = text.WithChanges(new TextChange(new TextSpan(start, find.Length), replace));
        return compilation.ReplaceSyntaxTree(tree, tree.WithChangedText(changed));
    }

    private static IEnumerable<IncrementalStepRunReason> Reasons(GeneratorDriver driver, string stepName) =>
        driver.GetRunResult().Results.Single().TrackedSteps[stepName]
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason);

    [Theory]
    [InlineData(1, "41", "42")]   // a file with no Vitreous types
    [InlineData(0, "new(1)", "new(2)")] // a handler's method body, same length
    public void EditThatCannotChangeTheOutput_DoesNotRegenerateSources(int file, string find, string replace)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Handlers, Unrelated);
        var driver = CreateDriver().RunGenerators(compilation);

        driver = driver.RunGenerators(Edit(compilation, file, find, replace));

        Reasons(driver, "VitreousDiscovery").Should().OnlyContain(static r =>
            r == IncrementalStepRunReason.Cached || r == IncrementalStepRunReason.Unchanged);
        Reasons(driver, "VitreousGeneratedSources").Should().OnlyContain(static r =>
            r == IncrementalStepRunReason.Cached || r == IncrementalStepRunReason.Unchanged);
        Reasons(driver, "VitreousReferencedTypes").Should().OnlyContain(static r =>
            r == IncrementalStepRunReason.Cached || r == IncrementalStepRunReason.Unchanged);
    }

    [Fact]
    public void AddingAHandler_RegeneratesSources()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Handlers, Unrelated);
        var driver = CreateDriver().RunGenerators(compilation);

        var added = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            using System.Threading;
            using System.Threading.Tasks;
            using Vitreous;
            using Vitreous.Abstractions;
            namespace TestProject;

            public sealed record Q2 : IQuery<int>;
            public sealed class Q2H : IQueryHandler<Q2, int>
            {
                public ValueTask<Result<int>> Handle(Q2 q, CancellationToken ct = default) => new(2);
            }
            """, path: "File2.cs"));
        driver = driver.RunGenerators(added);

        Reasons(driver, "VitreousGeneratedSources").Should().Contain(IncrementalStepRunReason.Modified);
        driver.GetRunResult().GeneratedTrees
            .Single(static t => t.FilePath.EndsWith("VitreousDispatcher.g.cs", StringComparison.Ordinal))
            .ToString().Should().Contain("TestProject.Q2");
    }

    [Fact]
    public void GeneratedOutput_HoldsNoRoslynObjects()
    {
        // Pipeline values must be plain data; symbols, syntax and compilations compare by
        // reference and would silently disable caching.
        var driver = CreateDriver().RunGenerators(GeneratorTestHelper.CreateCompilation(Handlers));

        foreach (var steps in driver.GetRunResult().Results.Single().TrackedSteps
                     .Where(static kv => kv.Key.StartsWith("Vitreous", StringComparison.Ordinal)))
        {
            foreach (var output in steps.Value.SelectMany(static s => s.Outputs))
            {
                output.Value.Should().NotBeAssignableTo<ISymbol>(steps.Key);
                output.Value.Should().NotBeAssignableTo<SyntaxNode>(steps.Key);
                output.Value.Should().NotBeAssignableTo<Compilation>(steps.Key);
            }
        }
    }
}
