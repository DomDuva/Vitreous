using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vitreous.Abstractions;
using VitreousClean._1.Cli.Common;
using VitreousClean._1.Cli.Orders;
using VitreousClean._1.Infrastructure;

namespace VitreousClean._1.Cli.Tests;

/// <summary>
/// Runs whole command lines through the app's own services: the generated dispatcher, the pipeline
/// behaviors, the handlers and the database. Only the output and the database file are replaced.
/// </summary>
public sealed class OrderCliTests : IAsyncLifetime
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
    private readonly StringWriter  _out       = new();
    private readonly StringWriter  _error     = new();
    private readonly IHost         _host;

    public OrderCliTests()
    {
        var builder = Program.CreateBuilder();
        builder.Configuration["ConnectionStrings:Database"] = $"Data Source={Path.Combine(_directory.FullName, "test.db")};Pooling=False";
        builder.Services.AddSingleton(new CliOutput(_out, _error));
        _host = builder.Build();
    }

    [Fact]
    public async Task A_placed_order_can_be_shown()
    {
        var id = await Place();

        Assert.Equal(ExitCodes.Success, await Run("show", id));

        Assert.Contains("99.80 EUR  ada@example.com", _out.ToString());
        Assert.Contains("2 x Keyboard at 49.90", _out.ToString());
    }

    [Fact]
    public async Task An_order_ships_once()
    {
        var id = await Place();

        Assert.Equal(ExitCodes.Success, await Run("ship", id));
        Assert.Equal(ExitCodes.Failure, await Run("ship", id));
        Assert.Contains("was shipped already", _error.ToString());

        _out.GetStringBuilder().Clear();
        await Run("list", "placed");
        Assert.Empty(_out.ToString());

        await Run("list", "shipped");
        Assert.Contains(id, _out.ToString());
    }

    [Theory]
    [InlineData("Keyboard", "0", "49.90")]
    [InlineData("Keyboard", "1", "-1")]
    [InlineData(" ", "1", "49.90")]
    public async Task An_invalid_order_is_rejected(string product, string quantity, string price)
    {
        Assert.Equal(ExitCodes.Usage, await Run("place", "ada@example.com", product, quantity, price));

        Assert.Contains("error:", _error.ToString());
    }

    [Theory]
    [InlineData("show")]
    [InlineData("ship")]
    public async Task An_unknown_order_fails(string verb)
    {
        Assert.Equal(ExitCodes.Failure, await Run(verb, Guid.NewGuid().ToString()));

        Assert.Contains("was not found", _error.ToString());
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("show", "not-an-id")]
    [InlineData("place", "ada@example.com", "Keyboard", "two", "49.90")]
    public async Task Wrong_arguments_print_the_usage(params string[] args)
    {
        Assert.Equal(ExitCodes.Usage, await Run(args));

        Assert.Contains("Usage:", _error.ToString());
    }

    public async ValueTask InitializeAsync() => await _host.Services.EnsureDatabaseCreatedAsync();

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        _directory.Delete(recursive: true);
        return ValueTask.CompletedTask;
    }

    // Prints the id of the new order, so a script can use it.
    private async Task<string> Place()
    {
        Assert.Equal(ExitCodes.Success, await Run("place", "ada@example.com", "Keyboard", "2", "49.90"));

        var id = _out.ToString().Trim();
        _out.GetStringBuilder().Clear();
        return id;
    }

    // A scope per run, as Program.cs does.
    private async Task<int> Run(params string[] args)
    {
        await using var scope = _host.Services.CreateAsyncScope();

        return await OrderCli.Run(
            args,
            scope.ServiceProvider.GetRequiredService<ISender>(),
            scope.ServiceProvider.GetRequiredService<CliOutput>(),
            TestContext.Current.CancellationToken);
    }
}
