using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Vitreous.SourceGenerationTests;

/// <summary>
/// Runs <see cref="Vitreous.SourceGeneration.VitreousGenerator"/> against an in-memory
/// Roslyn compilation built from the supplied source text and returns the generator's
/// diagnostics together with any source files it emitted.
/// </summary>
internal static class GeneratorTestHelper
{
    // Lazily-initialised set of metadata references used as the baseline for every
    // test compilation.  Built once and reused across all tests in the process.
    private static readonly Lazy<IReadOnlyList<MetadataReference>> s_defaultRefs =
        new(BuildDefaultReferences, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Collects metadata references for the BCL (via TRUSTED_PLATFORM_ASSEMBLIES),
    /// Vitreous itself, and any other assemblies already loaded in the test runner.
    /// </summary>
    private static IReadOnlyList<MetadataReference> BuildDefaultReferences()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refs = new List<MetadataReference>();

        // ── 1. BCL — use TRUSTED_PLATFORM_ASSEMBLIES (recommended in .NET Core) ────────
        // This is the only reliable way to obtain all BCL references in a hosted context;
        // AppDomain.CurrentDomain.GetAssemblies() often misses BCL types that are loaded
        // natively by the runtime rather than as managed assemblies.
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (tpa is not null)
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrEmpty(path) && seen.Add(path) && File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        // ── 2. Vitreous — must be explicit so the handler / command interfaces ─────────
        //       (ICommandHandler<,>, ICommand<>, etc.) are resolvable inside the
        //       in-memory test compilation.
        var vitreousLocation = typeof(Vitreous.Abstractions.ICommand<>).Assembly.Location;
        if (!string.IsNullOrEmpty(vitreousLocation) && seen.Add(vitreousLocation))
            refs.Add(MetadataReference.CreateFromFile(vitreousLocation));

        // Microsoft.Extensions.DependencyInjection(.Abstractions) — needed to compile the
        // generated registration code (ServiceDescriptor, TryAdd, GetRequiredService, ...).
        foreach (var t in new[]
                 {
                     typeof(Microsoft.Extensions.DependencyInjection.ServiceCollection),
                     typeof(Microsoft.Extensions.DependencyInjection.ServiceDescriptor),
                     typeof(Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions),
                 })
        {
            var loc = t.Assembly.Location;
            if (!string.IsNullOrEmpty(loc) && seen.Add(loc))
                refs.Add(MetadataReference.CreateFromFile(loc));
        }

        // ── 3. Any other managed assemblies already loaded in this test process ─────────
        //       Picks up Microsoft.Extensions.*, xunit, etc.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                continue;
            if (seen.Add(asm.Location))
                refs.Add(MetadataReference.CreateFromFile(asm.Location));
        }

        return refs;
    }

    /// <summary>
    /// Parses <paramref name="source"/>, compiles it with standard Vitreous references,
    /// runs <c>VitreousGenerator</c> over the result, and returns the generator's
    /// diagnostics and emitted source files.
    /// </summary>
    internal static (ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<GeneratedSourceResult> GeneratedSources)
        RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [syntaxTree],
            references: s_defaultRefs.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new Vitreous.SourceGeneration.VitreousGenerator();
        var driver    = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        var result = driver.GetRunResult();

        // Surface any unhandled generator exception as a clear test failure.
        var generatorException = result.Results.FirstOrDefault().Exception;
        if (generatorException is not null)
            throw new InvalidOperationException(
                "VitreousGenerator threw an unhandled exception during execution.",
                generatorException);

        return (
            result.Diagnostics,
            result.Results.SelectMany(static r => r.GeneratedSources).ToImmutableArray());
    }

    /// <summary>
    /// Runs the generator and then compiles the user source together with the generated
    /// output, returning the C# compiler errors (if any). Use this to assert that the
    /// generated code is actually valid for a given input shape.
    /// </summary>
    internal static ImmutableArray<Diagnostic> RunGeneratorAndCompile(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [syntaxTree],
            references: s_defaultRefs.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new Vitreous.SourceGeneration.VitreousGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        return updated.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    /// <summary>
    /// Creates a compilation of <paramref name="sources"/> (one syntax tree each, named
    /// <c>File0.cs</c>, <c>File1.cs</c>, …) with the standard Vitreous references.
    /// </summary>
    internal static CSharpCompilation CreateCompilation(params string[] sources) =>
        CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: sources.Select(static (s, i) => CSharpSyntaxTree.ParseText(s, path: $"File{i}.cs")),
            references: s_defaultRefs.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    /// <summary>
    /// Runs the generator over <paramref name="source"/> (compiled as <paramref name="assemblyName"/>
    /// with any <paramref name="extraReferences"/>) and returns the updated compilation together with
    /// every error: generator diagnostics and C# compiler errors alike.
    /// </summary>
    internal static (Compilation Compilation, ImmutableArray<Diagnostic> Errors) CompileWithGenerator(
        string source,
        string assemblyName = "TestAssembly",
        params MetadataReference[] extraReferences)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: s_defaultRefs.Value.Concat(extraReferences),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new Vitreous.SourceGeneration.VitreousGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);

        var errors = generatorDiagnostics.Concat(updated.GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        return (updated, errors);
    }

    /// <summary>
    /// Compiles <paramref name="source"/> with the generator, emits it, and loads it into a fresh
    /// load context so tests can call the generated <c>AddVitreousHandlers()</c> and dispatch through
    /// the generated dispatcher. Vitreous and DI types are shared with the test process.
    /// </summary>
    internal static Assembly EmitAndLoad(string source)
    {
        var (compilation, errors) = CompileWithGenerator(source, "TestAssembly_" + Guid.NewGuid().ToString("N"));
        if (!errors.IsEmpty)
            throw new InvalidOperationException("Compilation failed:\n" + string.Join("\n", errors));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
            throw new InvalidOperationException("Emit failed:\n" + string.Join("\n", emit.Diagnostics));

        stream.Position = 0;
        return new AssemblyLoadContext(name: null, isCollectible: true).LoadFromStream(stream);
    }

    /// <summary>
    /// Emits <paramref name="source"/> (with the generator) as a metadata reference named
    /// <paramref name="assemblyName"/>, for multi-project scenarios.
    /// </summary>
    internal static MetadataReference EmitAsReference(string source, string assemblyName)
    {
        var (compilation, errors) = CompileWithGenerator(source, assemblyName);
        if (!errors.IsEmpty)
            throw new InvalidOperationException("Compilation failed:\n" + string.Join("\n", errors));

        return compilation.ToMetadataReference();
    }
}
