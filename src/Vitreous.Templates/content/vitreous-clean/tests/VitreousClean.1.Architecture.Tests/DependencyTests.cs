using System.Reflection;
using VitreousClean._1.Application.Abstractions;
using VitreousClean._1.Domain.Orders;
using VitreousClean._1.Infrastructure.Persistence;

namespace VitreousClean._1.Architecture.Tests;

/// <summary>
/// The dependency rule: dependencies point inwards, towards the domain. A test fails as soon as a
/// layer uses a type from an assembly that isn't on its list. To allow a new one, add it here.
/// </summary>
public sealed class DependencyTests
{
    private static readonly Assembly Domain         = typeof(Order).Assembly;
    private static readonly Assembly Application    = typeof(IAppDbContext).Assembly;
    private static readonly Assembly Infrastructure = typeof(AppDbContext).Assembly;

    [Fact]
    public void Domain_uses_nothing_but_Vitreous() =>
        AssertUsesOnly(Domain, "Vitreous");

    [Fact]
    public void Application_uses_the_domain_and_no_database_provider() =>
        AssertUsesOnly(Application,
            Name(Domain),
            "Vitreous",
#if (UseValidation)
            "Vitreous.FluentValidation",
            "FluentValidation",
            "FluentValidation.DependencyInjectionExtensions",
#endif
#if (UseCaching)
            "Vitreous.Caching",
#endif
            "Microsoft.EntityFrameworkCore",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions");

    [Fact]
    public void Infrastructure_uses_the_inner_layers_and_not_the_host() =>
        Assert.Equal(
            new[] { Name(Application), Name(Domain) }.Order(),
            Uses(Infrastructure).Where(IsOwnAssembly).Order());

    private static void AssertUsesOnly(Assembly layer, params string[] allowed) =>
        Assert.Empty(Uses(layer).Except(allowed));

    // What the code uses, without the base class library.
    private static IEnumerable<string> Uses(Assembly layer) => layer.GetReferencedAssemblies()
        .Select(static a => a.Name!)
        .Where(static name => name is not ("netstandard" or "mscorlib") && !name.StartsWith("System.", StringComparison.Ordinal));

    private static bool IsOwnAssembly(string name) =>
        name.StartsWith(Name(Domain)[..^"Domain".Length], StringComparison.Ordinal);

    private static string Name(Assembly assembly) => assembly.GetName().Name!;
}
