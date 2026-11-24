#if (UseValidation)
using FluentValidation;
#endif
using Vitreous;
using Vitreous.Abstractions;
using VitreousConsole._1.Common;

namespace VitreousConsole._1.Features.Todos;

// A vertical slice: the command, its validator, its handler and its verb live in one file, so a
// feature is read, changed or deleted in one place. Add the next one from its folder with:
//   dotnet new vitreous-command -n <Name>

public sealed record CreateTodoCommand(string Title) : ICommand<TodoDto>;
#if (UseValidation)

// Runs before the handler. When it fails, the handler is skipped and the caller gets
// Error.Validation with the messages per property (printed to stderr, see ResultCliExtensions).
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
    public async ValueTask<Result<TodoDto>> Handle(CreateTodoCommand command, CancellationToken ct = default)
    {
#if (!UseValidation)
        // Expected failures are returned as an Error, not thrown.
        if (string.IsNullOrWhiteSpace(command.Title))
            return Error.Validation("A todo needs a title.");
        if (command.Title.Length > 200)
            return Error.Validation("A todo's title can't be longer than 200 characters.");

#endif
        return await store.Add(command.Title.Trim(), ct);
    }
}

public static class CreateTodoCli
{
    public static async Task<int> Run(string title, ISender sender, CliOutput output, CancellationToken ct) =>
        (await sender.Send(new CreateTodoCommand(title), ct))
        .ToExitCode(output, todo => output.Out.WriteLine($"Added {todo.Id}: {todo.Title}"));
}
