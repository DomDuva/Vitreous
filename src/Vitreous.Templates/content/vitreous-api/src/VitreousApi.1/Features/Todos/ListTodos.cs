using System.Runtime.CompilerServices;
using Vitreous;
using Vitreous.Abstractions;
using VitreousApi._1.Common;

namespace VitreousApi._1.Features.Todos;

// A stream query returns its items one at a time (IAsyncEnumerable), and ASP.NET Core writes
// them to the response as they arrive, so a large list never sits in memory all at once.
public sealed record ListTodosQuery : IStreamQuery<TodoDto>;

public sealed class ListTodosHandler(TodoStore store) : StreamQueryHandler<ListTodosQuery, TodoDto>
{
    protected override async IAsyncEnumerable<TodoDto> Stream(
        ListTodosQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var todo in store.All())
        {
            await Task.Yield(); // a database query awaits each row here
            yield return todo;
        }
    }
}

public static class ListTodosEndpoint
{
    public static void MapListTodos(this IEndpointRouteBuilder todos) =>
        todos.MapGet("/", static async (ISender sender, CancellationToken ct) =>
                (await sender.CreateStream(new ListTodosQuery(), ct)).ToHttpResult(static stream => Results.Ok(stream)))
            .WithName("ListTodos")
            .WithSummary("List every todo");
}
