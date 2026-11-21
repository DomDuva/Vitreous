using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Vitreous;
using Vitreous.Abstractions;
using Vitreous.Caching.Abstractions;

namespace Vitreous.Sample.Aot;

public sealed record TodoDto(Guid Id, string Title, bool Done);

public sealed class TodoStore
{
    private readonly ConcurrentDictionary<Guid, TodoDto> _todos = new();

    public TodoDto Add(string title)
    {
        var todo = new TodoDto(Guid.NewGuid(), title, false);
        _todos[todo.Id] = todo;
        return todo;
    }

    public TodoDto? Find(Guid id) => _todos.GetValueOrDefault(id);

    public TodoDto? Complete(Guid id)
    {
        if (!_todos.TryGetValue(id, out var todo))
            return null;
        return _todos[id] = todo with { Done = true };
    }

    public IEnumerable<TodoDto> All() => _todos.Values.OrderBy(static t => t.Title, StringComparer.Ordinal);
}

// Command with a result (class message)
public sealed record CreateTodoCommand(string Title) : ICommand<TodoDto>;

public sealed class CreateTodoHandler(TodoStore store, IPublisher publisher) : ICommandHandler<CreateTodoCommand, TodoDto>
{
    public async ValueTask<Result<TodoDto>> Handle(CreateTodoCommand command, CancellationToken ct = default)
    {
        var todo = store.Add(command.Title);
        await publisher.Publish(new TodoCreated(todo.Id, todo.Title), ct);
        return todo;
    }
}

// Command that evicts the cached todo
public sealed record CompleteTodoCommand(Guid Id) : ICommand<TodoDto>, IInvalidatesCache
{
    public IReadOnlyList<string> CacheKeys => [GetTodoQuery.KeyFor(Id)];
}

public sealed class CompleteTodoHandler(TodoStore store) : ICommandHandler<CompleteTodoCommand, TodoDto>
{
    public ValueTask<Result<TodoDto>> Handle(CompleteTodoCommand command, CancellationToken ct = default) =>
        new(store.Complete(command.Id) is { } todo ? todo : Error.NotFound($"Todo {command.Id} not found."));
}

// Cached query
public sealed record GetTodoQuery(Guid Id) : ICacheableQuery<TodoDto>
{
    public static string KeyFor(Guid id) => $"todo:{id}";

    public string CacheKey => KeyFor(Id);
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}

public sealed class GetTodoHandler(TodoStore store, HandlerCalls calls) : IQueryHandler<GetTodoQuery, TodoDto>
{
    public ValueTask<Result<TodoDto>> Handle(GetTodoQuery query, CancellationToken ct = default)
    {
        Interlocked.Increment(ref calls.GetTodo);
        return new(store.Find(query.Id) is { } todo ? todo : Error.NotFound($"Todo {query.Id} not found."));
    }
}

// Notification with two handlers, published in parallel
public sealed record TodoCreated(Guid Id, string Title) : INotification;

public sealed class CountCreatedTodos(HandlerCalls calls) : INotificationHandler<TodoCreated>
{
    public ValueTask Handle(TodoCreated notification, CancellationToken ct = default)
    {
        Interlocked.Increment(ref calls.Created);
        return default;
    }
}

public sealed class LogCreatedTodos(ILogger<LogCreatedTodos> logger) : INotificationHandler<TodoCreated>
{
    public ValueTask Handle(TodoCreated notification, CancellationToken ct = default)
    {
        logger.LogInformation("Created todo {Title}", notification.Title);
        return default;
    }
}

// Stream query
public sealed record ListTodosQuery : IStreamQuery<TodoDto>;

public sealed class ListTodosHandler(TodoStore store) : StreamQueryHandler<ListTodosQuery, TodoDto>
{
    protected override async IAsyncEnumerable<TodoDto> Stream(ListTodosQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var todo in store.All())
        {
            await Task.Yield();
            yield return todo;
        }
    }
}

// Closed pipeline behavior: rejects blank titles before the handler runs
public sealed class RequireTitleBehavior : IPipelineBehavior<CreateTodoCommand, TodoDto>
{
    public ValueTask<Result<TodoDto>> Handle(
        CreateTodoCommand message, MessageHandlerDelegate<CreateTodoCommand, TodoDto> next, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(message.Title)
            ? new(Error.Validation("A todo needs a title."))
            : next(message, ct);
}

/// <summary>Counts handler calls so the CI smoke test can tell cache hits from misses.</summary>
public sealed class HandlerCalls
{
    public int GetTodo;
    public int Created;
}
