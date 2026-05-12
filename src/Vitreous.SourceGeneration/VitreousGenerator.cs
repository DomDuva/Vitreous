using System.Collections.Immutable;
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
[Generator]
public sealed class VitreousGenerator : IIncrementalGenerator
{
    // Well-known handler interface names (unqualified generic arities)
    private const string CommandHandlerWithResult = "Vitreous.Abstractions.ICommandHandler`2";
    private const string CommandHandlerVoid       = "Vitreous.Abstractions.ICommandHandler`1";
    private const string QueryHandler             = "Vitreous.Abstractions.IQueryHandler`2";
    private const string NotificationHandler      = "Vitreous.Abstractions.INotificationHandler`1";
    private const string StreamQueryHandler       = "Vitreous.Abstractions.IStreamQueryHandler`2";

    // Well-known message interface names (used for VIT001 — no handler found)
    private const string ICommandWithResult  = "Vitreous.Abstractions.ICommand`1";
    private const string ICommandVoid        = "Vitreous.Abstractions.ICommand";
    private const string IQueryInterface     = "Vitreous.Abstractions.IQuery`1";
    private const string IStreamQueryInterface = "Vitreous.Abstractions.IStreamQuery`1";

    // [EnumeratorCancellation] attribute full name (used for VIT003)
    private const string EnumeratorCancellationAttribute =
        "System.Runtime.CompilerServices.EnumeratorCancellationAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Collect every class declaration in the compilation
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.Node)
            .Where(static c => c is not null);

        // Combine with the compilation so we can use semantic analysis
        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndClasses, static (spc, source) =>
            Execute(source.Left, source.Right, spc));
    }

    private static void Execute(
        Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> classes,
        SourceProductionContext context)
    {
        // Resolve handler interface symbols
        var iCommandHandlerWithResult = compilation.GetTypeByMetadataName(CommandHandlerWithResult);
        var iCommandHandlerVoid       = compilation.GetTypeByMetadataName(CommandHandlerVoid);
        var iQueryHandler             = compilation.GetTypeByMetadataName(QueryHandler);
        var iNotificationHandler      = compilation.GetTypeByMetadataName(NotificationHandler);
        var iStreamQueryHandler       = compilation.GetTypeByMetadataName(StreamQueryHandler);

        if (iCommandHandlerWithResult is null && iCommandHandlerVoid is null &&
            iQueryHandler is null && iNotificationHandler is null && iStreamQueryHandler is null)
        {
            // Vitreous.Abstractions not referenced — nothing to do
            return;
        }

        // Resolve message interface symbols (for VIT001)
        var iCommand         = compilation.GetTypeByMetadataName(ICommandWithResult);
        var iCommandVoidIf   = compilation.GetTypeByMetadataName(ICommandVoid);
        var iQuery           = compilation.GetTypeByMetadataName(IQueryInterface);
        var iStreamQuery     = compilation.GetTypeByMetadataName(IStreamQueryInterface);

        // Resolve [EnumeratorCancellation] for VIT003
        var enumeratorCancellationAttr = compilation.GetTypeByMetadataName(EnumeratorCancellationAttribute);

        var discovered    = new List<HandlerInfo>();
        var messageTypes  = new List<MessageTypeInfo>();

        // Track message types that have handlers (to detect missing / duplicate registrations)
        // key = messageFullName, value = list of handler full names
        var commandHandlers     = new Dictionary<string, List<string>>();
        var queryHandlers       = new Dictionary<string, List<string>>();
        var streamQueryHandlers = new Dictionary<string, List<string>>();

        // ── Pass 1: current project's syntax trees ────────────────────────────
        foreach (var classDecl in classes)
        {
            var model  = compilation.GetSemanticModel(classDecl.SyntaxTree);
            var symbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (symbol is null || symbol.IsAbstract || symbol.TypeKind != TypeKind.Class)
                continue;

            TryCollectHandler(
                symbol,
                iCommandHandlerWithResult,
                iCommandHandlerVoid,
                iQueryHandler,
                iNotificationHandler,
                iStreamQueryHandler,
                discovered,
                commandHandlers,
                queryHandlers,
                streamQueryHandlers);

            TryCollectMessageType(
                symbol,
                iCommand,
                iCommandVoidIf,
                iQuery,
                iStreamQuery,
                messageTypes,
                classDecl.GetLocation());

            // VIT003 — check for missing [EnumeratorCancellation] on stream handler methods
            if (iStreamQueryHandler is not null && enumeratorCancellationAttr is not null)
            {
                CheckEnumeratorCancellation(
                    symbol,
                    iStreamQueryHandler,
                    enumeratorCancellationAttr,
                    compilation,
                    context,
                    classDecl);
            }
        }

        // ── Pass 2: referenced assemblies that reference Vitreous ─────────────
        // Discovers handlers and message types in other projects (e.g. Application /
        // Infrastructure layers in a DDD solution) without requiring the source generator
        // to be installed on those projects.
        CollectFromReferencedAssemblies(
            compilation,
            iCommandHandlerWithResult,
            iCommandHandlerVoid,
            iQueryHandler,
            iNotificationHandler,
            iStreamQueryHandler,
            iCommand,
            iCommandVoidIf,
            iQuery,
            iStreamQuery,
            discovered,
            commandHandlers,
            queryHandlers,
            streamQueryHandlers,
            messageTypes);

        // Report VIT002 — ambiguous dispatch (multiple handlers per message)
        ReportDuplicateDiagnostics(context, commandHandlers);
        ReportDuplicateDiagnostics(context, queryHandlers);
        ReportDuplicateDiagnostics(context, streamQueryHandlers);

        // Report VIT001 — no handler found for a command/query/stream-query type
        ReportNoHandlerDiagnostics(context, messageTypes, commandHandlers, queryHandlers, streamQueryHandlers);

        // Emit source files
        var source1 = RegistrationEmitter.EmitServiceCollectionExtensions(discovered);
        context.AddSource("VitreousServiceCollectionExtensions.g.cs", source1);

        var source2 = RegistrationEmitter.EmitDispatcher(discovered);
        context.AddSource("VitreousDispatcher.g.cs", source2);
    }

    // -------------------------------------------------------------------------
    // Handler collection — shared by both the syntax and metadata paths
    // -------------------------------------------------------------------------

    /// <summary>
    /// Inspects <paramref name="symbol"/>'s implemented interfaces and records any
    /// Vitreous handler registrations into <paramref name="discovered"/>.
    /// </summary>
    private static void TryCollectHandler(
        INamedTypeSymbol symbol,
        INamedTypeSymbol? iCommandHandlerWithResult,
        INamedTypeSymbol? iCommandHandlerVoid,
        INamedTypeSymbol? iQueryHandler,
        INamedTypeSymbol? iNotificationHandler,
        INamedTypeSymbol? iStreamQueryHandler,
        List<HandlerInfo> discovered,
        Dictionary<string, List<string>> commandHandlers,
        Dictionary<string, List<string>> queryHandlers,
        Dictionary<string, List<string>> streamQueryHandlers)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            var original = iface.OriginalDefinition;

            // ICommandHandler<TCommand, TResult>
            if (iCommandHandlerWithResult is not null &&
                SymbolEqualityComparer.Default.Equals(original, iCommandHandlerWithResult))
            {
                var msgType    = iface.TypeArguments[0];
                var resultType = iface.TypeArguments[1];
                var info = new HandlerInfo(
                    HandlerFullName:  symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    HandlerNamespace: symbol.ContainingNamespace.ToDisplayString(),
                    HandlerClassName: symbol.Name,
                    MessageFullName:  msgType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    MessageTypeName:  msgType.Name,
                    Kind:             HandlerKind.Command,
                    ResultFullName:   resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""));

                discovered.Add(info);
                AddToMultimap(commandHandlers, info.MessageFullName, info.HandlerFullName);
                continue;
            }

            // ICommandHandler<TCommand> — void command (check arity to avoid matching the 2-arg version)
            if (iCommandHandlerVoid is not null &&
                SymbolEqualityComparer.Default.Equals(original, iCommandHandlerVoid) &&
                iface.TypeArguments.Length == 1)
            {
                // Ensure this is not the 2-arg variant being matched as 1-arg
                if (iCommandHandlerWithResult is not null &&
                    SymbolEqualityComparer.Default.Equals(original, iCommandHandlerWithResult))
                    continue;

                var msgType = iface.TypeArguments[0];
                var info = new HandlerInfo(
                    HandlerFullName:  symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    HandlerNamespace: symbol.ContainingNamespace.ToDisplayString(),
                    HandlerClassName: symbol.Name,
                    MessageFullName:  msgType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    MessageTypeName:  msgType.Name,
                    Kind:             HandlerKind.VoidCommand,
                    ResultFullName:   null);

                discovered.Add(info);
                AddToMultimap(commandHandlers, info.MessageFullName, info.HandlerFullName);
                continue;
            }

            // IQueryHandler<TQuery, TResult>
            if (iQueryHandler is not null &&
                SymbolEqualityComparer.Default.Equals(original, iQueryHandler))
            {
                var msgType    = iface.TypeArguments[0];
                var resultType = iface.TypeArguments[1];
                var info = new HandlerInfo(
                    HandlerFullName:  symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    HandlerNamespace: symbol.ContainingNamespace.ToDisplayString(),
                    HandlerClassName: symbol.Name,
                    MessageFullName:  msgType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    MessageTypeName:  msgType.Name,
                    Kind:             HandlerKind.Query,
                    ResultFullName:   resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""));

                discovered.Add(info);
                AddToMultimap(queryHandlers, info.MessageFullName, info.HandlerFullName);
                continue;
            }

            // INotificationHandler<TNotification>
            if (iNotificationHandler is not null &&
                SymbolEqualityComparer.Default.Equals(original, iNotificationHandler))
            {
                var msgType = iface.TypeArguments[0];
                discovered.Add(new HandlerInfo(
                    HandlerFullName:  symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    HandlerNamespace: symbol.ContainingNamespace.ToDisplayString(),
                    HandlerClassName: symbol.Name,
                    MessageFullName:  msgType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    MessageTypeName:  msgType.Name,
                    Kind:             HandlerKind.Notification,
                    ResultFullName:   null));
                continue;
            }

            // IStreamQueryHandler<TQuery, TResult>
            if (iStreamQueryHandler is not null &&
                SymbolEqualityComparer.Default.Equals(original, iStreamQueryHandler))
            {
                var msgType    = iface.TypeArguments[0];
                var resultType = iface.TypeArguments[1];
                var info = new HandlerInfo(
                    HandlerFullName:  symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    HandlerNamespace: symbol.ContainingNamespace.ToDisplayString(),
                    HandlerClassName: symbol.Name,
                    MessageFullName:  msgType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    MessageTypeName:  msgType.Name,
                    Kind:             HandlerKind.StreamQuery,
                    ResultFullName:   resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""));

                discovered.Add(info);
                AddToMultimap(streamQueryHandlers, info.MessageFullName, info.HandlerFullName);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Message type collection — used for VIT001 (no handler found)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Inspects <paramref name="symbol"/>'s implemented interfaces and records it
    /// as a message type if it implements <c>ICommand&lt;T&gt;</c>, <c>ICommand</c>,
    /// or <c>IQuery&lt;T&gt;</c>. Notifications are excluded — zero handlers is valid
    /// for notifications.
    /// </summary>
    private static void TryCollectMessageType(
        INamedTypeSymbol symbol,
        INamedTypeSymbol? iCommand,
        INamedTypeSymbol? iCommandVoid,
        INamedTypeSymbol? iQuery,
        INamedTypeSymbol? iStreamQuery,
        List<MessageTypeInfo> messageTypes,
        Location location)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            var original = iface.OriginalDefinition;

            // ICommand<TResult> — check before bare ICommand so the more specific match wins
            if (iCommand is not null &&
                SymbolEqualityComparer.Default.Equals(original, iCommand))
            {
                var resultType = iface.TypeArguments[0];
                messageTypes.Add(new MessageTypeInfo(
                    MessageFullName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    MessageTypeName: symbol.Name,
                    Kind:            HandlerKind.Command,
                    ResultFullName:  resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    Location:        location));
                return;
            }

            // IQuery<TResult>
            if (iQuery is not null &&
                SymbolEqualityComparer.Default.Equals(original, iQuery))
            {
                var resultType = iface.TypeArguments[0];
                messageTypes.Add(new MessageTypeInfo(
                    MessageFullName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    MessageTypeName: symbol.Name,
                    Kind:            HandlerKind.Query,
                    ResultFullName:  resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    Location:        location));
                return;
            }

            // IStreamQuery<TResult>
            if (iStreamQuery is not null &&
                SymbolEqualityComparer.Default.Equals(original, iStreamQuery))
            {
                var resultType = iface.TypeArguments[0];
                messageTypes.Add(new MessageTypeInfo(
                    MessageFullName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    MessageTypeName: symbol.Name,
                    Kind:            HandlerKind.StreamQuery,
                    ResultFullName:  resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    Location:        location));
                return;
            }

            // ICommand (void) — non-generic, no type arguments
            if (iCommandVoid is not null &&
                SymbolEqualityComparer.Default.Equals(original, iCommandVoid) &&
                iface.TypeArguments.Length == 0)
            {
                messageTypes.Add(new MessageTypeInfo(
                    MessageFullName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""),
                    MessageTypeName: symbol.Name,
                    Kind:            HandlerKind.VoidCommand,
                    ResultFullName:  null,
                    Location:        location));
                return;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Referenced-assembly scanning (multi-project / DDD support)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scans all referenced assemblies that directly reference the Vitreous core package
    /// and collects any handler implementations and message types found in them.
    /// </summary>
    private static void CollectFromReferencedAssemblies(
        Compilation compilation,
        INamedTypeSymbol? iCommandHandlerWithResult,
        INamedTypeSymbol? iCommandHandlerVoid,
        INamedTypeSymbol? iQueryHandler,
        INamedTypeSymbol? iNotificationHandler,
        INamedTypeSymbol? iStreamQueryHandler,
        INamedTypeSymbol? iCommand,
        INamedTypeSymbol? iCommandVoidIf,
        INamedTypeSymbol? iQuery,
        INamedTypeSymbol? iStreamQuery,
        List<HandlerInfo> discovered,
        Dictionary<string, List<string>> commandHandlers,
        Dictionary<string, List<string>> queryHandlers,
        Dictionary<string, List<string>> streamQueryHandlers,
        List<MessageTypeInfo> messageTypes)
    {
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol asm)
                continue;

            // Only scan assemblies that directly reference Vitreous — they are the only
            // ones that can contain handler implementations. This skips the BCL, ASP.NET
            // Core, and all unrelated NuGet packages automatically.
            if (!ReferencesVitreous(asm))
                continue;

            CollectFromNamespace(
                asm.GlobalNamespace,
                iCommandHandlerWithResult,
                iCommandHandlerVoid,
                iQueryHandler,
                iNotificationHandler,
                iStreamQueryHandler,
                iCommand,
                iCommandVoidIf,
                iQuery,
                iStreamQuery,
                discovered,
                commandHandlers,
                queryHandlers,
                streamQueryHandlers,
                messageTypes);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="asm"/> directly references the
    /// <c>Vitreous</c> core assembly. Any project that implements a Vitreous handler
    /// interface must reference it directly.
    /// </summary>
    private static bool ReferencesVitreous(IAssemblySymbol asm) =>
        asm.Modules.Any(static m =>
            m.ReferencedAssemblies.Any(static r =>
                string.Equals(r.Name, "Vitreous", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Recursively traverses <paramref name="ns"/> and calls
    /// <see cref="TryCollectHandler"/> and <see cref="TryCollectMessageType"/>
    /// for every non-abstract class found.
    /// </summary>
    private static void CollectFromNamespace(
        INamespaceSymbol ns,
        INamedTypeSymbol? iCommandHandlerWithResult,
        INamedTypeSymbol? iCommandHandlerVoid,
        INamedTypeSymbol? iQueryHandler,
        INamedTypeSymbol? iNotificationHandler,
        INamedTypeSymbol? iStreamQueryHandler,
        INamedTypeSymbol? iCommand,
        INamedTypeSymbol? iCommandVoidIf,
        INamedTypeSymbol? iQuery,
        INamedTypeSymbol? iStreamQuery,
        List<HandlerInfo> discovered,
        Dictionary<string, List<string>> commandHandlers,
        Dictionary<string, List<string>> queryHandlers,
        Dictionary<string, List<string>> streamQueryHandlers,
        List<MessageTypeInfo> messageTypes)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNs:
                    CollectFromNamespace(
                        childNs,
                        iCommandHandlerWithResult,
                        iCommandHandlerVoid,
                        iQueryHandler,
                        iNotificationHandler,
                        iStreamQueryHandler,
                        iCommand,
                        iCommandVoidIf,
                        iQuery,
                        iStreamQuery,
                        discovered,
                        commandHandlers,
                        queryHandlers,
                        streamQueryHandlers,
                        messageTypes);
                    break;

                case INamedTypeSymbol type
                    when !type.IsAbstract && type.TypeKind == TypeKind.Class:
                    TryCollectHandler(
                        type,
                        iCommandHandlerWithResult,
                        iCommandHandlerVoid,
                        iQueryHandler,
                        iNotificationHandler,
                        iStreamQueryHandler,
                        discovered,
                        commandHandlers,
                        queryHandlers,
                        streamQueryHandlers);
                    // Referenced assemblies have no syntax trees — use Location.None
                    TryCollectMessageType(
                        type,
                        iCommand,
                        iCommandVoidIf,
                        iQuery,
                        iStreamQuery,
                        messageTypes,
                        Location.None);
                    break;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Diagnostics
    // -------------------------------------------------------------------------

    /// <summary>
    /// Emits VIT002 for every message type that has more than one handler registered.
    /// </summary>
    private static void ReportDuplicateDiagnostics(
        SourceProductionContext context,
        Dictionary<string, List<string>> handlerMap)
    {
        foreach (var pair in handlerMap)
        {
            var messageType  = pair.Key;
            var handlerNames = pair.Value;
            if (handlerNames.Count <= 1)
                continue;

            var handlersStr = string.Join(", ", handlerNames.Select(n => $"'{n}'"));

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MultipleHandlersFound,
                Location.None,
                messageType,
                handlersStr));
        }
    }

    /// <summary>
    /// Emits VIT001 for every discovered command, query, or stream query that has no
    /// corresponding handler. Notifications are excluded — zero notification handlers is valid.
    /// </summary>
    private static void ReportNoHandlerDiagnostics(
        SourceProductionContext context,
        List<MessageTypeInfo> messageTypes,
        Dictionary<string, List<string>> commandHandlers,
        Dictionary<string, List<string>> queryHandlers,
        Dictionary<string, List<string>> streamQueryHandlers)
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

            var handlerKindWord = msg.Kind switch
            {
                HandlerKind.Query       => "Query",
                HandlerKind.StreamQuery => "StreamQuery",
                _                       => "Command"
            };
            var resultSuffix = msg.ResultFullName is not null ? $", {msg.ResultFullName}" : "";

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.NoHandlerFound,
                msg.Location,
                msg.MessageTypeName,
                handlerKindWord,
                resultSuffix));
        }
    }

    /// <summary>
    /// VIT003 — checks that every method on a stream query handler class that returns
    /// <c>IAsyncEnumerable&lt;T&gt;</c> and accepts a <c>CancellationToken</c> parameter
    /// decorates that parameter with <c>[EnumeratorCancellation]</c>.
    /// </summary>
    private static void CheckEnumeratorCancellation(
        INamedTypeSymbol symbol,
        INamedTypeSymbol iStreamQueryHandler,
        INamedTypeSymbol enumeratorCancellationAttr,
        Compilation compilation,
        SourceProductionContext context,
        ClassDeclarationSyntax classDecl)
    {
        // Only check classes that implement IStreamQueryHandler<,>
        var implementsStreamHandler = symbol.AllInterfaces.Any(iface =>
            SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, iStreamQueryHandler));

        if (!implementsStreamHandler) return;

        var cancellationTokenType = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        if (cancellationTokenType is null) return;

        // Inspect every method that returns IAsyncEnumerable<T>
        foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.ReturnType is not INamedTypeSymbol returnType) continue;

            // Check if return type is IAsyncEnumerable<T> (original definition = IAsyncEnumerable`1)
            var isAsyncEnumerable = returnType.OriginalDefinition.ToDisplayString() ==
                "System.Collections.Generic.IAsyncEnumerable<T>";
            if (!isAsyncEnumerable) continue;

            // Find a CancellationToken parameter missing [EnumeratorCancellation]
            foreach (var param in member.Parameters)
            {
                if (!SymbolEqualityComparer.Default.Equals(param.Type, cancellationTokenType))
                    continue;

                var hasAttr = param.GetAttributes().Any(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, enumeratorCancellationAttr));

                if (!hasAttr)
                {
                    // Find the method location in the syntax tree for a precise squiggle
                    var methodLocation = member.Locations.FirstOrDefault() ?? classDecl.GetLocation();
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.MissingEnumeratorCancellation,
                        methodLocation,
                        member.Name,
                        symbol.Name));
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void AddToMultimap(
        Dictionary<string, List<string>> map,
        string key,
        string value)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = new List<string>();
        list.Add(value);
    }
}
