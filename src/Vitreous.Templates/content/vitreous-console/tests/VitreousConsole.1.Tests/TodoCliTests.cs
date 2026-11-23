using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vitreous.Abstractions;
using VitreousConsole._1.Common;
using VitreousConsole._1.Features.Todos;

namespace VitreousConsole._1.Tests;

/// <summary>
/// Runs whole command lines through the app's own services: the generated dispatcher, the pipeline
/// behaviors and the handlers. Only the output and the file the todos are kept in are replaced.
/// </summary>
public sealed class TodoCliTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
    private readonly StringWriter  _out       = new();
    private readonly StringWriter  _error     = new();
    private readonly IHost         _host;

    public TodoCliTests()
    {
        var builder = Program.CreateBuilder();
        builder.Services.AddSingleton(new CliOutput(_out, _error));
        builder.Services.AddSingleton(new TodoStore(Path.Combine(_directory.FullName, "todos.json")));
        _host = builder.Build();
    }

    [Fact]
    public async Task An_added_todo_is_listed()
    {
        Assert.Equal(ExitCodes.Success, await Run("add", "Buy", "milk"));
        Assert.Equal(ExitCodes.Success, await Run("list"));

        Assert.Contains("1 [ ] Buy milk", _out.ToString());
    }

    [Fact]
    public async Task A_completed_todo_is_shown_as_done()
    {
        await Run("add", "Ship it");

        Assert.Equal(ExitCodes.Success, await Run("done", "1"));
        Assert.Equal(ExitCodes.Success, await Run("show", "1"));

        Assert.Contains("1 [x] Ship it", _out.ToString());
    }

    [Fact]
    public async Task Completing_a_todo_notifies_the_progress_handler()
    {
        await Run("add", "First");
        await Run("add", "Second");

        await Run("done", "1");

        Assert.Contains("Completed 1, 1 left to do", _out.ToString());
    }

    [Fact]
    public async Task A_todo_without_a_title_is_rejected()
    {
        Assert.Equal(ExitCodes.Usage, await Run("add"));

        Assert.Contains("error:", _error.ToString());
    }

    [Theory]
    [InlineData("show")]
    [InlineData("done")]
    public async Task An_unknown_todo_fails(string verb)
    {
        Assert.Equal(ExitCodes.Failure, await Run(verb, "42"));

        Assert.Contains("Todo 42 was not found.", _error.ToString());
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("done", "not-a-number")]
    public async Task Wrong_arguments_print_the_usage(params string[] args)
    {
        Assert.Equal(ExitCodes.Usage, await Run(args));

        Assert.Contains("Usage:", _error.ToString());
    }

    public void Dispose()
    {
        _host.Dispose();
        _directory.Delete(recursive: true);
    }

    // A scope per run, as Program.cs does.
    private async Task<int> Run(params string[] args)
    {
        await using var scope = _host.Services.CreateAsyncScope();

        return await TodoCli.Run(
            args,
            scope.ServiceProvider.GetRequiredService<ISender>(),
            scope.ServiceProvider.GetRequiredService<CliOutput>(),
            TestContext.Current.CancellationToken);
    }
}
