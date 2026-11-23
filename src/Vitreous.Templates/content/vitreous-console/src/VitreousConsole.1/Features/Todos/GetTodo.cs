using Vitreous;
using Vitreous.Abstractions;
using VitreousConsole._1.Common;

namespace VitreousConsole._1.Features.Todos;

public sealed record GetTodoQuery(int Id) : IQuery<TodoDto>;

public sealed class GetTodoHandler(TodoStore store) : IQueryHandler<GetTodoQuery, TodoDto>
{
    public async ValueTask<Result<TodoDto>> Handle(GetTodoQuery query, CancellationToken ct = default) =>
        await store.Find(query.Id, ct) is { } todo ? todo : Error.NotFound($"Todo {query.Id} was not found.");
}

public static class GetTodoCli
{
    public static async Task<int> Run(int id, ISender sender, CliOutput output, CancellationToken ct) =>
        (await sender.Send(new GetTodoQuery(id), ct)).ToExitCode(output, output.Out.WriteLine);
}
