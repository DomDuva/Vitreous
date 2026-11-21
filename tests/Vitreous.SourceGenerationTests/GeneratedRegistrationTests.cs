using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Vitreous.Abstractions;
using Xunit;

namespace Vitreous.SourceGenerationTests;

/// <summary>
/// The generated <c>AddVitreousHandlers()</c> registers core services without the reflection-based
/// sender when Vitreous has <c>AddVitreousGenerated</c>, so trimmed and Native AOT apps don't keep it.
/// </summary>
public class GeneratedRegistrationTests
{
    private const string Messages = """
        using System.Threading;
        using System.Threading.Tasks;
        using Vitreous;
        using Vitreous.Abstractions;
        namespace RegistrationProject;

        public sealed record Ping : ICommand<string>;
        public sealed class PingHandler : ICommandHandler<Ping, string>
        {
            public ValueTask<Result<string>> Handle(Ping c, CancellationToken ct = default) => new("pong");
        }
        """;

    [Fact]
    public void AddVitreousHandlers_CallsAddVitreousGenerated_WhenVitreousHasIt()
    {
        var (_, sources) = GeneratorTestHelper.RunGenerator(Messages);

        var extensions = sources.Single(static s => s.HintName == "VitreousServiceCollectionExtensions.g.cs").SourceText.ToString();
        extensions.Should().Contain("services.AddVitreousGenerated(options);")
            .And.NotContain("services.AddVitreous(options);");
    }

    [Fact]
    public async Task AddVitreousHandlers_NeverRegistersTheReflectionSender()
    {
        var assembly = GeneratorTestHelper.EmitAndLoad(Messages);
        var services = new ServiceCollection();
        assembly.GetType("Vitreous.VitreousGeneratedServiceCollectionExtensions", throwOnError: true)!
            .GetMethod("AddVitreousHandlers")!.Invoke(null, [services, null]);

        services.Should().NotContain(static d => d.ImplementationType != null && d.ImplementationType.Name == "ReflectionSender");

        using var provider = services.BuildServiceProvider();
        var sender = provider.CreateScope().ServiceProvider.GetRequiredService<ISender>();
        sender.GetType().Name.Should().Be("VitreousDispatcher");

        var ping = (ICommand<string>)Activator.CreateInstance(assembly.GetType("RegistrationProject.Ping", throwOnError: true)!)!;
        (await sender.Send(ping)).Value.Should().Be("pong");
    }

    [Fact]
    public void VitreousWithoutAddVitreousGenerated_CallsAddVitreous_AndCompiles()
    {
        // The generator may be upgraded without Vitreous; 0.3.0 has no AddVitreousGenerated.
        var path = typeof(GeneratedRegistrationTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static a => a.Key == "Vitreous030").Value!;

        var (sources, errors) = GeneratorTestHelper.CompileAgainstVitreous(Messages, MetadataReference.CreateFromFile(path));

        errors.Should().BeEmpty();
        var extensions = sources.Single(static s => s.HintName == "VitreousServiceCollectionExtensions.g.cs").SourceText.ToString();
        extensions.Should().Contain("services.AddVitreous(options);")
            .And.NotContain("AddVitreousGenerated");
    }
}
