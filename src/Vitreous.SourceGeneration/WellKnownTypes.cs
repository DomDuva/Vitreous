using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Vitreous.SourceGeneration;

/// <summary>
/// The Vitreous and BCL type symbols the generator compares against, resolved once per
/// <see cref="Compilation"/> rather than once per analysed type declaration.
/// </summary>
internal sealed class WellKnownTypes
{
    private static readonly ConditionalWeakTable<Compilation, WellKnownTypes?> s_cache = new();

    private WellKnownTypes(Compilation compilation)
    {
        CommandHandlerWithResult = compilation.GetTypeByMetadataName("Vitreous.Abstractions.ICommandHandler`2");
        CommandHandlerVoid       = compilation.GetTypeByMetadataName("Vitreous.Abstractions.ICommandHandler`1");
        QueryHandler             = compilation.GetTypeByMetadataName("Vitreous.Abstractions.IQueryHandler`2");
        NotificationHandler      = compilation.GetTypeByMetadataName("Vitreous.Abstractions.INotificationHandler`1");
        StreamQueryHandler       = compilation.GetTypeByMetadataName("Vitreous.Abstractions.IStreamQueryHandler`2");

        Command     = compilation.GetTypeByMetadataName("Vitreous.Abstractions.ICommand`1");
        CommandVoid = compilation.GetTypeByMetadataName("Vitreous.Abstractions.ICommand");
        Query       = compilation.GetTypeByMetadataName("Vitreous.Abstractions.IQuery`1");
        StreamQuery = compilation.GetTypeByMetadataName("Vitreous.Abstractions.IStreamQuery`1");

        EnumeratorCancellationAttribute =
            compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.EnumeratorCancellationAttribute");
        CancellationToken = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
    }

    // Handler interfaces
    public INamedTypeSymbol? CommandHandlerWithResult { get; }
    public INamedTypeSymbol? CommandHandlerVoid { get; }
    public INamedTypeSymbol? QueryHandler { get; }
    public INamedTypeSymbol? NotificationHandler { get; }
    public INamedTypeSymbol? StreamQueryHandler { get; }

    // Message interfaces (VIT001)
    public INamedTypeSymbol? Command { get; }
    public INamedTypeSymbol? CommandVoid { get; }
    public INamedTypeSymbol? Query { get; }
    public INamedTypeSymbol? StreamQuery { get; }

    // VIT003
    public INamedTypeSymbol? EnumeratorCancellationAttribute { get; }
    public INamedTypeSymbol? CancellationToken { get; }

    /// <summary>
    /// Returns the well-known types for <paramref name="compilation"/>, or <see langword="null"/>
    /// when it does not reference Vitreous (nothing to generate).
    /// </summary>
    public static WellKnownTypes? Get(Compilation compilation) =>
        s_cache.GetValue(compilation, static c =>
        {
            var types = new WellKnownTypes(c);
            return types.CommandHandlerWithResult is null && types.CommandHandlerVoid is null &&
                   types.QueryHandler is null && types.NotificationHandler is null &&
                   types.StreamQueryHandler is null
                ? null
                : types;
        });
}
