using VitreousConsole._1.Features.Todos;

namespace VitreousConsole._1.Tests;

/// <summary>
/// Handlers are plain classes: create one with its dependencies and call Handle, no host needed.
/// </summary>
public sealed class CreateTodoTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_handler_stores_the_todo()
    {
        var store   = CreateStore();
        var handler = new CreateTodoHandler(store);

        var result = await handler.Handle(new CreateTodoCommand("Buy milk"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("Buy milk", (await store.Find(result.Value.Id, Ct))?.Title);
    }
#if (UseValidation)

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task The_validator_rejects_a_blank_title(string title)
    {
        var result = await new CreateTodoValidator().ValidateAsync(new CreateTodoCommand(title), Ct);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTodoCommand.Title));
    }
#else

    [Fact]
    public async Task The_handler_rejects_a_blank_title()
    {
        var result = await new CreateTodoHandler(CreateStore()).Handle(new CreateTodoCommand(" "), Ct);

        Assert.Equal("VALIDATION", result.Error.Code);
    }
#endif

    public void Dispose() => _directory.Delete(recursive: true);

    private TodoStore CreateStore() => new(Path.Combine(_directory.FullName, "todos.json"));
}
