using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Vitreous.Abstractions;
using VitreousClean._1.Application.Abstractions;
using VitreousClean._1.Application.Orders;
using VitreousClean._1.Infrastructure;
using VitreousClean._1.Worker.Orders;

namespace VitreousClean._1.Worker.Tests;

/// <summary>
/// Runs the worker with the app's own services: the generated dispatcher, the pipeline behaviors,
/// the handlers and the database. The clock is replaced, so no test waits for real time to pass,
/// and so is the email sender.
/// </summary>
public sealed class ShippingWorkerTests : IAsyncLifetime
{
    private readonly DirectoryInfo    _directory = Directory.CreateTempSubdirectory();
    private readonly FakeTimeProvider _time      = new();
    private readonly FakeEmailSender  _email     = new();
    private readonly IHost            _host;
    private readonly ShippingWorker   _worker;

    public ShippingWorkerTests()
    {
        // Development validates scopes, as dotnet run does: resolving ISender outside one fails.
        var builder = Program.CreateBuilder(["--environment=Development"]);
        builder.Configuration["ConnectionStrings:Database"] = $"Data Source={Path.Combine(_directory.FullName, "test.db")};Pooling=False";
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddSingleton<IEmailSender>(_email);

        _host   = builder.Build();
        _worker = _host.Services.GetServices<IHostedService>().OfType<ShippingWorker>().Single();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Placed_orders_are_shipped_and_their_customers_told()
    {
        var id = await Place("ada@example.com");

        await _worker.RunOnce(Ct);

        Assert.Equal("Shipped", (await Send(new GetOrderQuery(id))).Status);
        Assert.Contains(_email.Sent, e => e.To == "ada@example.com" && e.Subject.Contains("on its way"));
    }

    [Fact]
    public async Task An_order_that_fails_does_not_stop_the_others()
    {
        // The email fails after the order is saved, so the worker sees an exception.
        await Place("ada@example.com");
        await Place("grace@example.com");
        _email.FailFor = "ada@example.com";

        await _worker.RunOnce(Ct);

        Assert.Contains(_email.Sent, e => e.To == "grace@example.com" && e.Subject.Contains("on its way"));
    }

    [Fact]
    public async Task The_worker_polls_until_the_host_stops()
    {
        await _host.StartAsync(Ct);

        // Placed while the worker runs, so only a later tick can pick it up.
        await Place("ada@example.com");
        await AdvanceUntil(() => _email.Sent.Any(e => e.Subject.Contains("on its way")));

        await _host.StopAsync(Ct);
        Assert.True(_worker.ExecuteTask!.IsCompletedSuccessfully);

        var late = await Place("ada@example.com");
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("Placed", (await Send(new GetOrderQuery(late))).Status);
    }

    public async ValueTask InitializeAsync() => await _host.Services.EnsureDatabaseCreatedAsync();

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        _directory.Delete(recursive: true);
        return ValueTask.CompletedTask;
    }

    private async Task<Guid> Place(string customerEmail)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var command = new PlaceOrderCommand(customerEmail, "EUR", [new PlaceOrderLine("Keyboard", 1, 49.90m)]);

        return (await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Ct)).Value;
    }

    private async Task<T> Send<T>(IQuery<T> query)
    {
        await using var scope = _host.Services.CreateAsyncScope();

        return (await scope.ServiceProvider.GetRequiredService<ISender>().Send(query, Ct)).Value;
    }

    // The worker runs on another thread: move the fake clock a step, then let it catch up.
    private async Task AdvanceUntil(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            _time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(10, timeout.Token);
        }
    }
}
