using System.Collections.Concurrent;

namespace VitreousApi._1.Features.Todos;

public sealed record TodoDto(Guid Id, string Title, bool Done);

/// <summary>
/// Keeps the todos in memory, so the project runs with no setup. When you add a database, the
/// handlers use your EF Core DbContext directly in place of this class.
/// </summary>
public sealed class TodoStore
{
    private readonly ConcurrentDictionary<Guid, TodoDto> _todos = new();

    public TodoDto Add(string title)
    {
        var todo = new TodoDto(Guid.NewGuid(), title, Done: false);
        _todos[todo.Id] = todo;
        return todo;
    }

    public TodoDto? Find(Guid id) => _todos.GetValueOrDefault(id);

    public bool Complete(Guid id)
    {
        while (_todos.TryGetValue(id, out var todo))
        {
            if (_todos.TryUpdate(id, todo with { Done = true }, todo))
                return true;
        }

        return false;
    }

    public IEnumerable<TodoDto> All() => _todos.Values.OrderBy(static t => t.Title, StringComparer.Ordinal);
}
