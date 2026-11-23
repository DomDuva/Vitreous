namespace VitreousApi._1.Features.Todos;

public static class TodoEndpoints
{
    /// <summary>Maps every endpoint of the Todos feature under <c>/todos</c>.</summary>
    public static IEndpointRouteBuilder MapTodos(this IEndpointRouteBuilder app)
    {
        var todos = app.MapGroup("/todos").WithTags("Todos");

        todos.MapCreateTodo();
        todos.MapGetTodo();
        todos.MapListTodos();
        todos.MapCompleteTodo();

        return app;
    }
}
