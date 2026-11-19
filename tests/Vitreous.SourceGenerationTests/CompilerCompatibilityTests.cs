using FluentAssertions;
using Vitreous.SourceGeneration;
using Xunit;

namespace Vitreous.SourceGenerationTests;

/// <summary>
/// The compiler only loads an analyzer built against a Roslyn version it is at least as new as.
/// Anything newer is skipped with warning CS9057 and no source is generated.
/// </summary>
public class CompilerCompatibilityTests
{
    // Roslyn 4.8 ships with the .NET 8.0.100 SDK, the oldest SDK for the oldest target framework.
    private static readonly Version MaxSupportedRoslyn = new(4, 8, 0, 0);

    [Theory]
    [InlineData("Microsoft.CodeAnalysis")]
    [InlineData("Microsoft.CodeAnalysis.CSharp")]
    public void Generator_references_a_Roslyn_version_the_NET_8_SDK_can_load(string assemblyName)
    {
        var reference = typeof(VitreousGenerator).Assembly
            .GetReferencedAssemblies()
            .Single(a => a.Name == assemblyName);

        reference.Version.Should().BeLessThanOrEqualTo(MaxSupportedRoslyn,
            "the generator must load in every SDK that can build net8.0 projects");
    }
}
