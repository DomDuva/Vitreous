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

internal enum HandlerKind
{
    Command,        // ICommandHandler<TCommand, TResult>
    VoidCommand,    // ICommandHandler<TCommand>
    Query,          // IQueryHandler<TQuery, TResult>
    Notification    // INotificationHandler<TNotification>
}
