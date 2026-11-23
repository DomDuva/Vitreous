using Vitreous.Abstractions;
using VitreousConsole._1.Common;

namespace VitreousConsole._1.Features.Todos;

// A notification goes to every handler registered for it: zero, one or many. Add another
// INotificationHandler<TodoCompleted> anywhere in the project and the generator wires it up.
public sealed record TodoCompleted(int Id) : INotification;

public sealed class PrintProgress(TodoStore store, CliOutput output) : INotificationHandler<TodoCompleted>
{
    public async ValueTask Handle(TodoCompleted notification, CancellationToken ct = default)
    {
        var remaining = (await store.All(ct)).Count(static t => !t.Done);

        output.Out.WriteLine($"Completed {notification.Id}, {remaining} left to do");
    }
}
