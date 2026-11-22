using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Vitreous.SourceGeneration;

// Every model in this file is an equatable record of strings / enums / EquatableArray so that
// incremental pipeline steps can be compared by value. None of them may hold an ISymbol,
// SyntaxNode, Location or Compilation: those compare by reference and would defeat caching.

/// <summary>
/// Represents a discovered handler and the message/result types it handles.
/// </summary>
internal sealed record HandlerInfo(
    string HandlerFullName,
    string MessageFullName,
    HandlerKind Kind,
    string? ResultFullName,   // null for ICommand (void) and INotification handlers
    LocationInfo? Location,   // null for handlers discovered in referenced assemblies
    TypeFacts? MessageFacts,  // for closing open-generic behaviors; null if the type can't be described
    TypeFacts? ResultFacts    // null for ICommand (void) and INotification handlers
);

/// <summary>
/// Represents a discovered command or query message type.
/// Used to detect messages with no corresponding handler (VIT001).
/// </summary>
internal sealed record MessageTypeInfo(
    string MessageFullName,
    string MessageTypeName,
    HandlerKind Kind,
    string? ResultFullName,                    // null for ICommand (void)
    LocationInfo? Location,                    // null for types discovered in referenced assemblies
    EquatableArray<string> BaseTypeFullNames   // nearest first; a base type's handler also handles this message
);

internal enum HandlerKind
{
    Command,        // ICommandHandler<TCommand, TResult>
    VoidCommand,    // ICommandHandler<TCommand>
    Query,          // IQueryHandler<TQuery, TResult>
    Notification,   // INotificationHandler<TNotification>
    StreamQuery     // IStreamQueryHandler<TQuery, TResult>
}

/// <summary>
/// A value-equatable stand-in for a source <see cref="Location"/>.
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(Location? location) =>
        location is { IsInSource: true, SourceTree: { } tree }
            ? new LocationInfo(tree.FilePath, location.SourceSpan, location.GetLineSpan().Span)
            : null;
}

/// <summary>
/// A diagnostic to report, kept as data until the output stage.
/// </summary>
internal sealed record DiagnosticInfo(string Id, LocationInfo? Location, EquatableArray<string> Arguments);

/// <summary>
/// Everything discovered from one type declaration in the current compilation.
/// </summary>
internal sealed record TypeModel(
    EquatableArray<HandlerInfo> Handlers,
    EquatableArray<MessageTypeInfo> Messages,
    EquatableArray<DiagnosticInfo> Diagnostics,
    EquatableArray<BehaviorInfo> Behaviors,
    TypeFacts? Notification);

/// <summary>
/// Handlers and messages discovered in referenced assemblies.
/// <see cref="VitreousReferenced"/> is <see langword="false"/> when the compilation does not
/// reference Vitreous at all, in which case nothing is generated.
/// </summary>
internal sealed record ReferenceModel(
    bool VitreousReferenced,
    CoreFeatures Features,
    EquatableArray<HandlerInfo> Handlers,
    EquatableArray<MessageTypeInfo> Messages,
    BehaviorModel Behaviors)
{
    public static readonly ReferenceModel NotReferenced =
        new(false, CoreFeatures.None, EquatableArray<HandlerInfo>.Empty, EquatableArray<MessageTypeInfo>.Empty, BehaviorModel.Empty);
}

/// <summary>
/// Core APIs newer than the oldest supported Vitreous that the referenced Vitreous provides.
/// The generator only emits calls to the ones present, so upgrading the generator without
/// upgrading Vitreous still compiles.
/// </summary>
[Flags]
internal enum CoreFeatures
{
    None                 = 0,
    DispatchTelemetry    = 1 << 0,   // Vitreous.Internal.DispatchTelemetry (0.4.0)
    AddVitreousGenerated = 1 << 1,   // VitreousServiceCollectionExtensions.AddVitreousGenerated (0.4.0)
    GeneratedBehaviors   = 1 << 2,   // Vitreous.Internal.GeneratedBehaviors + AddVitreousGeneratedBehaviors (0.4.0)
}

/// <summary>
/// The merged result of discovery: what to generate and which diagnostics to report.
/// </summary>
internal sealed record DiscoveryResult(
    bool VitreousReferenced,
    CoreFeatures Features,
    EquatableArray<HandlerInfo> Handlers,
    EquatableArray<DiagnosticInfo> Diagnostics,
    BehaviorModel Behaviors);

/// <summary>A generated file: hint name and source text.</summary>
internal sealed record GeneratedFile(string HintName, string Source);
