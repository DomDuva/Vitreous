using System.Text.Json;

namespace VitreousConsole._1.Features.Todos;

public sealed record TodoDto(int Id, string Title, bool Done)
{
    public override string ToString() => $"{Id,3} [{(Done ? 'x' : ' ')}] {Title}";
}

/// <summary>
/// Keeps the todos in a JSON file, so they are still there on the next run and the project needs
/// no setup. When you add a database, the handlers use your EF Core DbContext directly in place
/// of this class.
/// </summary>
public sealed class TodoStore(string path)
{
    /// <summary>Used unless the <c>TodosFile</c> setting names another file.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VitreousConsole.1", "todos.json");

    public async Task<IReadOnlyList<TodoDto>> All(CancellationToken ct)
    {
        if (!File.Exists(path))
            return [];

        await using var file = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<TodoDto>>(file, cancellationToken: ct) ?? [];
    }

    public async Task<TodoDto?> Find(int id, CancellationToken ct) =>
        (await All(ct)).FirstOrDefault(t => t.Id == id);

    public async Task<TodoDto> Add(string title, CancellationToken ct)
    {
        var todos = await All(ct);
        var todo  = new TodoDto(todos.Select(static t => t.Id).DefaultIfEmpty().Max() + 1, title, Done: false);

        await Save([.. todos, todo], ct);
        return todo;
    }

    public async Task<bool> Complete(int id, CancellationToken ct)
    {
        var todos = await All(ct);
        if (todos.All(t => t.Id != id))
            return false;

        await Save([.. todos.Select(t => t.Id == id ? t with { Done = true } : t)], ct);
        return true;
    }

    private async Task Save(List<TodoDto> todos, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // Written to a second file first, so cancelling halfway can't lose the todos.
        var temporary = path + ".tmp";
        await using (var file = File.Create(temporary))
            await JsonSerializer.SerializeAsync(file, todos, cancellationToken: ct);

        File.Move(temporary, path, overwrite: true);
    }
}
