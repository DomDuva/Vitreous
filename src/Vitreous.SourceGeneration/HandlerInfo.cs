using Microsoft.CodeAnalysis;

namespace Vitreous.SourceGeneration;

/// <summary>
/// Represents a discovered handler and the message/result types it handles.
/// </summary>
internal sealed record HandlerInfo(
    string HandlerFullName,
    string HandlerNamespace,
    string HandlerClassName,
    string MessageFullName,
    string MessageTypeName,
    HandlerKind Kind,
    string? ResultFullName    // null for ICommand (void) and INotification handlers
);

/// <summary>
/// Represents a discovered command or query message type.
/// Used to detect messages with no corresponding handler (VIT001).
/// </summary>
internal sealed record MessageTypeInfo(
    string MessageFullName,
    string MessageTypeName,
    HandlerKind Kind,
    string? ResultFullName,  // null for ICommand (void)
    Location Location        // Location.None for types discovered in referenced assemblies
);

internal enum HandlerKind
{
    Command,        // ICommandHandler<TCommand, TResult>
    VoidCommand,    // ICommandHandler<TCommand>
    Query,          // IQueryHandler<TQuery, TResult>
    Notification,   // INotificationHandler<TNotification>
    StreamQuery     // IStreamQueryHandler<TQuery, TResult>
}
