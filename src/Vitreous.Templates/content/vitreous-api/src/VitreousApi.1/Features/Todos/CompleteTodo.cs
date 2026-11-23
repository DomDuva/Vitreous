using Vitreous;
using Vitreous.Abstractions;
#if (UseCaching)
using Vitreous.Caching.Abstractions;
#endif
using VitreousApi._1.Common;

namespace VitreousApi._1.Features.Todos;

#if (UseCaching)
// A command without a result: ICommand, and the handler returns Result. IInvalidatesCache evicts
// the cached GetTodoQuery once the command succeeds, so the next read sees the change.
public sealed record CompleteTodoCommand(Guid Id) : ICommand, IInvalidatesCache
{
    public IReadOnlyList<string> CacheKeys => [GetTodoQuery.CacheKeyFor(Id)];
}
#else
// A command without a result: ICommand, and the handler returns Result.
public sealed record CompleteTodoCommand(Guid Id) : ICommand;
#endif

public sealed class CompleteTodoHandler(TodoStore store, IPublisher publisher) : ICommandHandler<CompleteTodoCommand>
{
    public async ValueTask<Result> Handle(CompleteTodoCommand command, CancellationToken ct = default)
    {
        if (!store.Complete(command.Id))
            return Error.NotFound($"Todo {command.Id} was not found.");

        // Other features react to the change through a notification, without this handler
        // knowing who listens.
        await publisher.Publish(new TodoCompleted(command.Id), ct);
        return Result.Success;
    }
}

public static class CompleteTodoEndpoint
{
    public static void MapCompleteTodo(this IEndpointRouteBuilder todos) =>
        todos.MapPost("/{id:guid}/complete", static async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CompleteTodoCommand(id), ct)).ToHttpResult(static () => Results.NoContent()))
            .WithName("CompleteTodo")
            .WithSummary("Mark a todo as done");
}
