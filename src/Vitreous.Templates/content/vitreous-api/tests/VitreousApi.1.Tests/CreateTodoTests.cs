using VitreousApi._1.Features.Todos;

namespace VitreousApi._1.Tests;

/// <summary>
/// Handlers are plain classes: create one with its dependencies and call Handle, no host needed.
/// </summary>
public sealed class CreateTodoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_handler_stores_the_todo()
    {
        var store   = new TodoStore();
        var handler = new CreateTodoHandler(store);

        var result = await handler.Handle(new CreateTodoCommand("Buy milk"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("Buy milk", store.Find(result.Value.Id)?.Title);
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
        var result = await new CreateTodoHandler(new TodoStore()).Handle(new CreateTodoCommand(" "), Ct);

        Assert.Equal("VALIDATION", result.Error.Code);
    }
#endif
}
