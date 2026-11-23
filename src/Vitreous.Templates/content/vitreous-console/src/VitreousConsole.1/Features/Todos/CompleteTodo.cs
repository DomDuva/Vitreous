using Vitreous;
using Vitreous.Abstractions;
using VitreousConsole._1.Common;

namespace VitreousConsole._1.Features.Todos;

// A command without a result: ICommand, and the handler returns Result.
public sealed record CompleteTodoCommand(int Id) : ICommand;

public sealed class CompleteTodoHandler(TodoStore store, IPublisher publisher) : ICommandHandler<CompleteTodoCommand>
{
    public async ValueTask<Result> Handle(CompleteTodoCommand command, CancellationToken ct = default)
    {
        if (!await store.Complete(command.Id, ct))
            return Error.NotFound($"Todo {command.Id} was not found.");

        // Other features react to the change through a notification, without this handler
        // knowing who listens.
        await publisher.Publish(new TodoCompleted(command.Id), ct);
        return Result.Success;
    }
}

public static class CompleteTodoCli
{
    public static async Task<int> Run(int id, ISender sender, CliOutput output, CancellationToken ct) =>
        (await sender.Send(new CompleteTodoCommand(id), ct)).ToExitCode(output);
}
