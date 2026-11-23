using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using VitreousWorker._1.Features.Reminders;

namespace VitreousWorker._1.Tests;

/// <summary>
/// Runs the worker with the app's own services: the generated dispatcher, the pipeline behaviors
/// and the handlers. Only the clock is replaced, so no test waits for real time to pass.
/// </summary>
public sealed class ReminderWorkerTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly IHost            _host;
    private readonly ReminderWorker   _worker;
    private readonly ReminderStore    _store;

    public ReminderWorkerTests()
    {
        // Development validates scopes, as dotnet run does: resolving ISender outside one fails.
        var builder = Program.CreateBuilder(["--environment=Development"]);
        builder.Services.AddSingleton<TimeProvider>(_time);

        _host   = builder.Build();
        _worker = _host.Services.GetServices<IHostedService>().OfType<ReminderWorker>().Single();
        _store  = _host.Services.GetRequiredService<ReminderStore>();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reminder_is_sent_once_it_is_due()
    {
        _store.Add("ada", "Stand up", _time.GetUtcNow().AddMinutes(1));

        await _worker.RunOnce(Ct);
        Assert.Equal(1, _store.PendingCount);

        _time.Advance(TimeSpan.FromMinutes(1));

        await _worker.RunOnce(Ct);
        Assert.Equal(0, _store.PendingCount);
    }

    [Fact]
    public async Task A_reminder_that_fails_stays_pending_and_does_not_stop_the_others()
    {
        var unknown = _store.Add("nobody", "Who is this for?", _time.GetUtcNow());
        var invalid = _store.Add("ada", "", _time.GetUtcNow());
        _store.Add("grace", "Stand up", _time.GetUtcNow());

        await _worker.RunOnce(Ct);

        Assert.Equal(
            new[] { unknown.Id, invalid.Id }.Order(),
            _store.DueAt(_time.GetUtcNow()).Select(r => r.Id).Order());
    }

    [Fact]
    public async Task The_worker_polls_until_the_host_stops()
    {
        await _host.StartAsync(Ct);

        // Added while the worker runs, so only a later tick can pick it up.
        _store.Add("ada", "Stand up", _time.GetUtcNow().AddMinutes(1));
        await AdvanceUntil(() => _store.PendingCount == 0);

        await _host.StopAsync(Ct);
        _store.Add("ada", "Too late", _time.GetUtcNow());
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.True(_worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(1, _store.PendingCount);
    }

    public void Dispose() => _host.Dispose();

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
