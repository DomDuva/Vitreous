#if (UseValidation)
using FluentValidation;
#endif
using Vitreous;
using Vitreous.Abstractions;
using VitreousApi._1.Common;

namespace VitreousApi._1.Features.Todos;

// A vertical slice: the command, its validator, its handler and its endpoint live in one file, so
// a feature is read, changed or deleted in one place. Add the next one with:
//   dotnet new vitreous-command -n <Name>

public sealed record CreateTodoCommand(string Title) : ICommand<TodoDto>;
#if (UseValidation)

// Runs before the handler. When it fails, the handler is skipped and the caller gets
// Error.Validation with the messages per property (a 400 with "errors", see ResultHttpExtensions).
public sealed class CreateTodoValidator : AbstractValidator<CreateTodoCommand>
{
    public CreateTodoValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
    }
}
#endif

public sealed class CreateTodoHandler(TodoStore store) : ICommandHandler<CreateTodoCommand, TodoDto>
{
    public ValueTask<Result<TodoDto>> Handle(CreateTodoCommand command, CancellationToken ct = default)
    {
#if (!UseValidation)
        // Expected failures are returned as an Error, not thrown.
        if (string.IsNullOrWhiteSpace(command.Title))
            return new(Error.Validation("A todo needs a title."));
        if (command.Title.Length > 200)
            return new(Error.Validation("A todo's title can't be longer than 200 characters."));

#endif
        return new(store.Add(command.Title.Trim()));
    }
}

public static class CreateTodoEndpoint
{
    public static void MapCreateTodo(this IEndpointRouteBuilder todos) =>
        todos.MapPost("/", static async (CreateTodoCommand command, ISender sender, CancellationToken ct) =>
                (await sender.Send(command, ct)).ToHttpResult(static todo => Results.Created($"/todos/{todo.Id}", todo)))
            .WithName("CreateTodo")
            .WithSummary("Create a todo");
}
