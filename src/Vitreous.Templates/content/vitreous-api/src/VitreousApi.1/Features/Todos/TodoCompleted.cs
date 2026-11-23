using Vitreous.Abstractions;

namespace VitreousApi._1.Features.Todos;

// A notification goes to every handler registered for it: zero, one or many. Add another
// INotificationHandler<TodoCompleted> anywhere in the project and the generator wires it up.
public sealed record TodoCompleted(Guid Id) : INotification;

public sealed class LogTodoCompleted(ILogger<LogTodoCompleted> logger) : INotificationHandler<TodoCompleted>
{
    public ValueTask Handle(TodoCompleted notification, CancellationToken ct = default)
    {
        logger.LogInformation("Todo {TodoId} completed", notification.Id);
        return ValueTask.CompletedTask;
    }
}
