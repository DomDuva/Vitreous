using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vitreous.SourceGeneration;

/// <summary>
/// Roslyn incremental source generator that discovers Vitreous handler implementations
/// and emits:
/// <list type="bullet">
///   <item>A <c>AddVitreousHandlers(IServiceCollection)</c> extension method.</item>
///   <item>A concrete <c>VitreousDispatcher</c> class with per-type, reflection-free dispatch.</item>
/// </list>
/// Discovery covers both the current project's source files and any referenced assembly
/// that directly references the Vitreous core package, enabling multi-project / DDD solutions
/// without any additional configuration.
/// </summary>
/// <remarks>
/// The pipeline is incremental end to end:
/// <list type="number">
///   <item>Each type declaration with a base list is turned into an equatable <see cref="TypeModel"/>.
///         Only declarations in edited files are re-analysed.</item>
///   <item>Referenced assemblies are scanned into a <see cref="ReferenceModel"/>, cached per
///         <see cref="MetadataReference"/>, so unchanged references are never walked twice.</item>
///   <item>Both are merged into a <see cref="DiscoveryResult"/>; generated sources are produced from it
///         and are skipped entirely when it compares equal to the previous run.</item>
/// </list>
/// </remarks>
[Generator]
public sealed class VitreousGenerator : IIncrementalGenerator
{
    /// <summary>Step names, for incremental-caching tests.</summary>
    internal static class TrackingNames
    {
        public const string SourceTypes      = "VitreousSourceTypes";
        public const string ReferencedTypes  = "VitreousReferencedTypes";
        public const string Discovery        = "VitreousDiscovery";
        public const string GeneratedSources = "VitreousGeneratedSources";
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Messages are usually records (and may be structs); handlers are classes or records.
        // Anything that implements a Vitreous interface has a base list in at least one part.
        var sourceTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax &&
                    ((TypeDeclarationSyntax)node).BaseList is not null,
                transform: static (ctx, ct) => TransformType(ctx, ct))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .WithTrackingName(TrackingNames.SourceTypes)
            .Collect();

        var referencedTypes = context.CompilationProvider
            .Select(static (compilation, ct) => ScanReferences(compilation, ct))
            .WithTrackingName(TrackingNames.ReferencedTypes);

        var discovery = sourceTypes
            .Combine(referencedTypes)
            .Select(static (pair, _) => Merge(pair.Left, pair.Right))
            .WithTrackingName(TrackingNames.Discovery);

        var generatedSources = discovery
            .Select(static (result, _) => Emit(result))
            .WithTrackingName(TrackingNames.GeneratedSources);

        context.RegisterSourceOutput(generatedSources, static (spc, files) =>
        {
            foreach (var file in files)
                spc.AddSource(file.HintName, file.Source);
        });

        // Diagnostics need real Locations, which must point into the current compilation's
        // syntax trees. Mapping them back is cheap, so this output may run on every compilation
        // without affecting the cached source generation above.
        var diagnostics = discovery.Select(static (result, _) => result.Diagnostics);

        context.RegisterSourceOutput(diagnostics.Combine(context.CompilationProvider), static (spc, pair) =>
            ReportDiagnostics(spc, pair.Left, pair.Right));
    }

    // -------------------------------------------------------------------------
    // Pipeline steps
    // -------------------------------------------------------------------------

    private static TypeModel? TransformType(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol symbol)
            return null;

        var types = WellKnownTypes.Get(ctx.SemanticModel.Compilation);
        if (types is null || symbol.IsAbstract)
            return null;

        // The generated dispatcher lives in this assembly, so internal types are fine,
        // but private/protected nested types cannot be referenced from it.
        if (!IsAccessibleFromGeneratedCode(symbol))
            return null;

        var handlers = symbol.TypeKind == TypeKind.Class && !symbol.IsGenericType
            ? CollectHandlers(symbol, types, LocationInfo.From(SourceLocation(symbol)))
            : EquatableArray<HandlerInfo>.Empty;

        var messages = CollectMessageType(symbol, types, LocationInfo.From(ctx.Node.GetLocation()));

        var diagnostics = symbol.TypeKind == TypeKind.Class
            ? CheckEnumeratorCancellation(symbol, types)
            : EquatableArray<DiagnosticInfo>.Empty;

        if (handlers.Count == 0 && messages.Count == 0 && diagnostics.Count == 0)
            return null;

        return new TypeModel(handlers, messages, diagnostics);
    }

    // Per-reference cache: a MetadataReference object is reused across compilations while the
    // reference is unchanged, so its handlers/messages are scanned once instead of per keystroke.
    private static readonly ConditionalWeakTable<MetadataReference, ReferenceScan> s_referenceScans = new();

    private sealed class ReferenceScan
    {
        public ReferenceScan(EquatableArray<HandlerInfo> handlers, EquatableArray<MessageTypeInfo> messages)
        {
            Handlers = handlers;
            Messages = messages;
        }

        public EquatableArray<HandlerInfo> Handlers { get; }

        public EquatableArray<MessageTypeInfo> Messages { get; }
    }

    private static ReferenceModel ScanReferences(Compilation compilation, CancellationToken ct)
    {
        var types = WellKnownTypes.Get(compilation);
        if (types is null)
            return ReferenceModel.NotReferenced;

        var handlers = ImmutableArray.CreateBuilder<HandlerInfo>();
        var messages = ImmutableArray.CreateBuilder<MessageTypeInfo>();

        foreach (var reference in compilation.References)
        {
            ct.ThrowIfCancellationRequested();

            var scan = s_referenceScans.GetValue(reference, r => ScanReference(compilation, r, types, ct));
            handlers.AddRange(scan.Handlers);
            messages.AddRange(scan.Messages);
        }

        var features = types.DispatchTelemetry is not null ? CoreFeatures.DispatchTelemetry : CoreFeatures.None;

        return new ReferenceModel(true, features, new(handlers.ToImmutable()), new(messages.ToImmutable()));
    }

    private static ReferenceScan ScanReference(
        Compilation compilation,
        MetadataReference reference,
        WellKnownTypes types,
        CancellationToken ct)
    {
        // Only assemblies that directly reference Vitreous can contain handler implementations.
        // This skips the BCL, ASP.NET Core and unrelated packages without walking them.
        if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly ||
            !ReferencesVitreous(assembly))
        {
            return new ReferenceScan(EquatableArray<HandlerInfo>.Empty, EquatableArray<MessageTypeInfo>.Empty);
        }

        var handlers = new List<HandlerInfo>();
        var messages = new List<MessageTypeInfo>();
        CollectFromNamespace(assembly.GlobalNamespace, types, handlers, messages, ct);
        return new ReferenceScan(handlers.ToEquatableArray(), messages.ToEquatableArray());
    }

    private static DiscoveryResult Merge(ImmutableArray<TypeModel> sourceTypes, ReferenceModel references)
    {
        if (!references.VitreousReferenced)
            return new DiscoveryResult(false, CoreFeatures.None, EquatableArray<HandlerInfo>.Empty, EquatableArray<DiagnosticInfo>.Empty);

        // Partial types produce one model per part that has a base list; keep each entry once.
        var handlers = sourceTypes.SelectMany(static t => t.Handlers)
            .Concat(references.Handlers)
            .GroupBy(static h => (h.HandlerFullName, h.MessageFullName, h.Kind))
            .Select(static g => g.First())
            .ToList();

        var messages = sourceTypes.SelectMany(static t => t.Messages)
            .Concat(references.Messages)
            .GroupBy(static m => m.MessageFullName)
            .Select(static g => g.First())
            .ToList();

        var diagnostics = new List<DiagnosticInfo>();
        diagnostics.AddRange(sourceTypes.SelectMany(static t => t.Diagnostics).Distinct());

        var commandHandlers     = GroupByMessage(handlers, HandlerKind.Command, HandlerKind.VoidCommand);
        var queryHandlers       = GroupByMessage(handlers, HandlerKind.Query);
        var streamQueryHandlers = GroupByMessage(handlers, HandlerKind.StreamQuery);

        // VIT002 — ambiguous dispatch (multiple handlers per message)
        AddDuplicateDiagnostics(diagnostics, commandHandlers);
        AddDuplicateDiagnostics(diagnostics, queryHandlers);
        AddDuplicateDiagnostics(diagnostics, streamQueryHandlers);

        // VIT001 — no handler found for a command/query/stream-query type
        AddNoHandlerDiagnostics(diagnostics, messages, commandHandlers, queryHandlers, streamQueryHandlers);

        return new DiscoveryResult(true, references.Features, handlers.ToEquatableArray(), diagnostics.ToEquatableArray());
    }

    private static EquatableArray<GeneratedFile> Emit(DiscoveryResult result)
    {
        if (!result.VitreousReferenced)
            return EquatableArray<GeneratedFile>.Empty;

        var handlers = result.Handlers.AsImmutableArray();
        return new[]
        {
            new GeneratedFile("VitreousServiceCollectionExtensions.g.cs", RegistrationEmitter.EmitServiceCollectionExtensions(handlers)),
            new GeneratedFile("VitreousDispatcher.g.cs", RegistrationEmitter.EmitDispatcher(handlers, result.Features)),
        }.ToEquatableArray();
    }

    private static void ReportDiagnostics(
        SourceProductionContext context,
        EquatableArray<DiagnosticInfo> diagnostics,
        Compilation compilation)
    {
        if (diagnostics.Count == 0)
            return;

        var trees = new Dictionary<string, SyntaxTree>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (!trees.ContainsKey(tree.FilePath))
                trees[tree.FilePath] = tree;
        }

        foreach (var info in diagnostics)
        {
            var location = info.Location is { } l && trees.TryGetValue(l.FilePath, out var tree)
                ? Location.Create(tree, l.Span)
                : Location.None;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ById(info.Id),
                location,
                info.Arguments.Cast<object?>().ToArray()));
        }
    }

    // -------------------------------------------------------------------------
    // Handler collection — shared by both the syntax and metadata paths
    // -------------------------------------------------------------------------

    /// <summary>
    /// Inspects <paramref name="symbol"/>'s implemented interfaces and returns any
    /// Vitreous handler registrations it provides.
    /// </summary>
    private static EquatableArray<HandlerInfo> CollectHandlers(
        INamedTypeSymbol symbol,
        WellKnownTypes types,
        LocationInfo? location)
    {
        List<HandlerInfo>? found = null;
        var handlerName = FullName(symbol);

        foreach (var iface in symbol.AllInterfaces)
        {
            var original = iface.OriginalDefinition;

            HandlerKind kind;
            if (Is(original, types.CommandHandlerWithResult)) kind = HandlerKind.Command;
            else if (Is(original, types.CommandHandlerVoid))  kind = HandlerKind.VoidCommand;
            else if (Is(original, types.QueryHandler))        kind = HandlerKind.Query;
            else if (Is(original, types.NotificationHandler)) kind = HandlerKind.Notification;
            else if (Is(original, types.StreamQueryHandler))  kind = HandlerKind.StreamQuery;
            else continue;

            var resultName = iface.TypeArguments.Length == 2 ? FullName(iface.TypeArguments[1]) : null;

            (found ??= new List<HandlerInfo>()).Add(new HandlerInfo(
                HandlerFullName: handlerName,
                MessageFullName: FullName(iface.TypeArguments[0]),
                Kind:            kind,
                ResultFullName:  resultName,
                Location:        location));
        }

        return found is null ? EquatableArray<HandlerInfo>.Empty : found.ToEquatableArray();
    }

    /// <summary>
    /// Returns <paramref name="symbol"/> as a message type if it implements <c>ICommand&lt;T&gt;</c>,
    /// <c>ICommand</c>, <c>IQuery&lt;T&gt;</c> or <c>IStreamQuery&lt;T&gt;</c>. Notifications are
    /// excluded — zero handlers is valid for notifications.
    /// </summary>
    private static EquatableArray<MessageTypeInfo> CollectMessageType(
        INamedTypeSymbol symbol,
        WellKnownTypes types,
        LocationInfo? location)
    {
        // Open generic message types cannot be dispatched (no concrete type to match on).
        if (symbol.IsGenericType)
            return EquatableArray<MessageTypeInfo>.Empty;

        foreach (var iface in symbol.AllInterfaces)
        {
            var original = iface.OriginalDefinition;

            HandlerKind kind;
            if (Is(original, types.Command))          kind = HandlerKind.Command;
            else if (Is(original, types.Query))       kind = HandlerKind.Query;
            else if (Is(original, types.StreamQuery)) kind = HandlerKind.StreamQuery;
            else if (Is(original, types.CommandVoid)) kind = HandlerKind.VoidCommand;
            else continue;

            return new[]
            {
                new MessageTypeInfo(
                    MessageFullName:   FullName(symbol),
                    MessageTypeName:   symbol.Name,
                    Kind:              kind,
                    ResultFullName:    iface.TypeArguments.Length == 1 ? FullName(iface.TypeArguments[0]) : null,
                    Location:          location,
                    BaseTypeFullNames: BaseTypeNames(symbol))
            }.ToEquatableArray();
        }

        return EquatableArray<MessageTypeInfo>.Empty;
    }

    // -------------------------------------------------------------------------
    // Referenced-assembly scanning (multi-project / DDD support)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="asm"/> directly references the
    /// <c>Vitreous</c> core assembly. Any project that implements a Vitreous handler
    /// interface must reference it directly.
    /// </summary>
    private static bool ReferencesVitreous(IAssemblySymbol asm) =>
        asm.Modules.Any(static m =>
            m.ReferencedAssemblies.Any(static r =>
                string.Equals(r.Name, "Vitreous", StringComparison.OrdinalIgnoreCase)));

    private static void CollectFromNamespace(
        INamespaceSymbol ns,
        WellKnownTypes types,
        List<HandlerInfo> handlers,
        List<MessageTypeInfo> messages,
        CancellationToken ct)
    {
        foreach (var member in ns.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            switch (member)
            {
                case INamespaceSymbol childNs:
                    CollectFromNamespace(childNs, types, handlers, messages, ct);
                    break;

                case INamedTypeSymbol type:
                    CollectFromType(type, types, handlers, messages);
                    break;
            }
        }
    }

    /// <summary>
    /// Collects handlers / message types from <paramref name="type"/> and recurses into
    /// its nested types (handlers are commonly nested inside a static feature class).
    /// </summary>
    private static void CollectFromType(
        INamedTypeSymbol type,
        WellKnownTypes types,
        List<HandlerInfo> handlers,
        List<MessageTypeInfo> messages)
    {
        // Only public types can be referenced from the generated code in another assembly.
        if (type.DeclaredAccessibility != Accessibility.Public)
            return;

        if (!type.IsAbstract && !type.IsGenericType)
        {
            // Referenced assemblies have no syntax trees, so there is no location.
            if (type.TypeKind == TypeKind.Class)
                handlers.AddRange(CollectHandlers(type, types, location: null));

            if (type.TypeKind is TypeKind.Class or TypeKind.Struct)
                messages.AddRange(CollectMessageType(type, types, location: null));
        }

        foreach (var nested in type.GetTypeMembers())
            CollectFromType(nested, types, handlers, messages);
    }

    // -------------------------------------------------------------------------
    // Diagnostics
    // -------------------------------------------------------------------------

    private static Dictionary<string, List<HandlerInfo>> GroupByMessage(
        List<HandlerInfo> handlers,
        params HandlerKind[] kinds)
    {
        var map = new Dictionary<string, List<HandlerInfo>>();
        foreach (var handler in handlers)
        {
            if (Array.IndexOf(kinds, handler.Kind) < 0)
                continue;

            if (!map.TryGetValue(handler.MessageFullName, out var list))
                map[handler.MessageFullName] = list = new List<HandlerInfo>();
            list.Add(handler);
        }
        return map;
    }

    /// <summary>
    /// Adds VIT002 for every message type that has more than one handler registered,
    /// once per handler declared in source (so each one gets a squiggle), or once without
    /// a location when every handler lives in a referenced assembly.
    /// </summary>
    private static void AddDuplicateDiagnostics(
        List<DiagnosticInfo> diagnostics,
        Dictionary<string, List<HandlerInfo>> handlerMap)
    {
        foreach (var pair in handlerMap)
        {
            var handlers = pair.Value;
            if (handlers.Count <= 1)
                continue;

            var arguments = new[]
            {
                pair.Key,
                string.Join(", ", handlers.Select(static h => $"'{h.HandlerFullName}'")),
            }.ToEquatableArray();

            var locations = handlers.Select(static h => h.Location).Where(static l => l is not null).ToList();
            if (locations.Count == 0)
                locations.Add(null);

            foreach (var location in locations)
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MultipleHandlersFound.Id, location, arguments));
        }
    }

    /// <summary>
    /// Adds VIT001 for every discovered command, query, or stream query that has no
    /// corresponding handler. Notifications are excluded — zero notification handlers is valid.
    /// </summary>
    private static void AddNoHandlerDiagnostics(
        List<DiagnosticInfo> diagnostics,
        List<MessageTypeInfo> messageTypes,
        Dictionary<string, List<HandlerInfo>> commandHandlers,
        Dictionary<string, List<HandlerInfo>> queryHandlers,
        Dictionary<string, List<HandlerInfo>> streamQueryHandlers)
    {
        foreach (var msg in messageTypes)
        {
            var map = msg.Kind switch
            {
                HandlerKind.Query       => queryHandlers,
                HandlerKind.StreamQuery => streamQueryHandlers,
                _                       => commandHandlers
            };

            if (map.ContainsKey(msg.MessageFullName))
                continue;

            // The dispatcher falls back to the nearest base type that has a handler.
            if (msg.BaseTypeFullNames.Any(map.ContainsKey))
                continue;

            var handlerKindWord = msg.Kind switch
            {
                HandlerKind.Query       => "Query",
                HandlerKind.StreamQuery => "StreamQuery",
                _                       => "Command"
            };
            var resultSuffix = msg.ResultFullName is not null ? $", {msg.ResultFullName}" : "";

            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.NoHandlerFound.Id,
                msg.Location,
                new[] { msg.MessageTypeName, handlerKindWord, resultSuffix }.ToEquatableArray()));
        }
    }

    /// <summary>
    /// VIT003 — checks that every async iterator on a stream query handler class that returns
    /// <c>IAsyncEnumerable&lt;T&gt;</c> and accepts a <c>CancellationToken</c> parameter
    /// decorates that parameter with <c>[EnumeratorCancellation]</c>.
    /// </summary>
    private static EquatableArray<DiagnosticInfo> CheckEnumeratorCancellation(
        INamedTypeSymbol symbol,
        WellKnownTypes types)
    {
        if (types.StreamQueryHandler is null ||
            types.EnumeratorCancellationAttribute is null ||
            types.CancellationToken is null)
        {
            return EquatableArray<DiagnosticInfo>.Empty;
        }

        // Only check classes that implement IStreamQueryHandler<,>
        if (!symbol.AllInterfaces.Any(iface => Is(iface.OriginalDefinition, types.StreamQueryHandler)))
            return EquatableArray<DiagnosticInfo>.Empty;

        List<DiagnosticInfo>? found = null;

        foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.ReturnType is not INamedTypeSymbol returnType) continue;

            var isAsyncEnumerable = returnType.OriginalDefinition.ToDisplayString() ==
                "System.Collections.Generic.IAsyncEnumerable<T>";
            if (!isAsyncEnumerable) continue;

            // [EnumeratorCancellation] only has an effect on async iterators. On any other
            // method the compiler reports CS8424, so requiring it there would be unfixable.
            if (!IsAsyncIterator(member)) continue;

            foreach (var param in member.Parameters)
            {
                if (!SymbolEqualityComparer.Default.Equals(param.Type, types.CancellationToken))
                    continue;

                var hasAttr = param.GetAttributes().Any(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, types.EnumeratorCancellationAttribute));

                if (!hasAttr)
                {
                    (found ??= new List<DiagnosticInfo>()).Add(new DiagnosticInfo(
                        DiagnosticDescriptors.MissingEnumeratorCancellation.Id,
                        LocationInfo.From(member.Locations.FirstOrDefault()),
                        new[] { member.Name, symbol.Name }.ToEquatableArray()));
                }
            }
        }

        return found is null ? EquatableArray<DiagnosticInfo>.Empty : found.ToEquatableArray();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool Is(INamedTypeSymbol candidate, INamedTypeSymbol? wellKnown) =>
        wellKnown is not null && SymbolEqualityComparer.Default.Equals(candidate, wellKnown);

    // Keeps nullable annotations (IQuery<List<string?>>) so the emitted handler types match the
    // user's declarations; dropping them raises CS8631 in nullable-enabled projects.
    private static readonly SymbolDisplayFormat s_fullNameFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static string FullName(ITypeSymbol symbol) =>
        symbol.ToDisplayString(s_fullNameFormat).Replace("global::", "");

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="symbol"/> and all of its containing
    /// types are public or internal, i.e. referenceable from generated code in the same assembly.
    /// </summary>
    private static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol symbol)
    {
        for (INamedTypeSymbol? t = symbol; t is not null; t = t.ContainingType)
        {
            if (t.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                return false;
        }
        return true;
    }

    /// <summary>Fully-qualified names of the base classes of <paramref name="symbol"/>, nearest first.</summary>
    private static EquatableArray<string> BaseTypeNames(INamedTypeSymbol symbol)
    {
        var names = new List<string>();
        for (var t = symbol.BaseType; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
            names.Add(FullName(t));
        return names.ToEquatableArray();
    }

    /// <summary>The first source location of <paramref name="symbol"/>, or <see langword="null"/>.</summary>
    private static Location? SourceLocation(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(static l => l.IsInSource);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="method"/> is declared in source as an
    /// <c>async</c> iterator, i.e. has the <c>async</c> modifier and a <c>yield</c> statement in
    /// its own body (not in a nested local function or lambda).
    /// </summary>
    private static bool IsAsyncIterator(IMethodSymbol method)
    {
        if (!method.IsAsync)
            return false;

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var yields = reference.GetSyntax()
                .DescendantNodes(static n =>
                    n is not LocalFunctionStatementSyntax and not AnonymousFunctionExpressionSyntax)
                .OfType<YieldStatementSyntax>();

            if (yields.Any())
                return true;
        }

        return false;
    }
}
