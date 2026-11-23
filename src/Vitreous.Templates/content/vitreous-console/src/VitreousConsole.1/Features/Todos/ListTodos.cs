using System.Runtime.CompilerServices;
using Vitreous;
using Vitreous.Abstractions;
using VitreousConsole._1.Common;

namespace VitreousConsole._1.Features.Todos;

// A stream query returns its items one at a time (IAsyncEnumerable), and the app prints them as
// they arrive, so a large list never sits in memory all at once.
public sealed record ListTodosQuery : IStreamQuery<TodoDto>;

public sealed class ListTodosHandler(TodoStore store) : StreamQueryHandler<ListTodosQuery, TodoDto>
{
    protected override async IAsyncEnumerable<TodoDto> Stream(
        ListTodosQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var todo in await store.All(ct)) // a database query awaits each row here
            yield return todo;
    }
}

public static class ListTodosCli
{
    public static async Task<int> Run(ISender sender, CliOutput output, CancellationToken ct)
    {
        var result = await sender.CreateStream(new ListTodosQuery(), ct);
        if (result.IsFailure)
            return result.Error.ToExitCode(output);

        await foreach (var todo in result.Value.WithCancellation(ct))
            output.Out.WriteLine(todo);

        return ExitCodes.Success;
    }
}
