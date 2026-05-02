using Microsoft.CodeAnalysis;

namespace Vitreous.SourceGeneration;

/// <summary>
/// Build-time diagnostic descriptors reported by the Vitreous source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "Vitreous";

    /// <summary>
    /// VIT001 — A command or query type has no registered handler.
    /// </summary>
    internal static readonly DiagnosticDescriptor NoHandlerFound = new(
        id: "VIT001",
        title: "No Vitreous handler found",
        messageFormat: "No handler is registered for '{0}'. Implement 'I{1}Handler<{0}{2}>' in any project that references Vitreous.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Every ICommand<T>, ICommand, and IQuery<T> should have exactly one handler registered.");

    /// <summary>
    /// VIT002 — A command or query type has more than one handler (ambiguous dispatch).
    /// </summary>
    internal static readonly DiagnosticDescriptor MultipleHandlersFound = new(
        id: "VIT002",
        title: "Multiple Vitreous handlers found",
        messageFormat: "Multiple handlers found for '{0}': {1}. Dispatch is ambiguous — remove all but one handler.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each ICommand<T>, ICommand, and IQuery<T> must have at most one handler.");
}
