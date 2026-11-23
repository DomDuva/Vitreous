using Vitreous;
using Vitreous.Abstractions;
#if (UseCaching)
using Vitreous.Caching.Abstractions;
#endif
using VitreousApi._1.Common;

namespace VitreousApi._1.Features.Todos;

#if (UseCaching)
// ICacheableQuery: the caching behavior returns the stored result for CacheKey and only runs the
// handler on a miss. CompleteTodoCommand evicts the key when the todo changes.
public sealed record GetTodoQuery(Guid Id) : ICacheableQuery<TodoDto>
{
    public static string CacheKeyFor(Guid id) => $"todos:{id}";

    public string CacheKey => CacheKeyFor(Id);
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}
#else
public sealed record GetTodoQuery(Guid Id) : IQuery<TodoDto>;
#endif

public sealed class GetTodoHandler(TodoStore store) : IQueryHandler<GetTodoQuery, TodoDto>
{
    public ValueTask<Result<TodoDto>> Handle(GetTodoQuery query, CancellationToken ct = default) =>
        new(store.Find(query.Id) is { } todo ? todo : Error.NotFound($"Todo {query.Id} was not found."));
}

public static class GetTodoEndpoint
{
    public static void MapGetTodo(this IEndpointRouteBuilder todos) =>
        todos.MapGet("/{id:guid}", static async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetTodoQuery(id), ct)).ToHttpResult(static todo => Results.Ok(todo)))
            .WithName("GetTodo")
            .WithSummary("Get a todo by id");
}
