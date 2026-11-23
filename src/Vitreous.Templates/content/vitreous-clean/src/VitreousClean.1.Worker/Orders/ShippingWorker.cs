using Microsoft.Extensions.Options;
using Vitreous.Abstractions;
using VitreousClean._1.Application.Orders;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Worker.Orders;

public sealed class ShippingOptions
{
    /// <summary>How often the worker looks for orders to ship.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Looks for placed orders on a timer and sends a command to ship each. It only decides when work
/// happens: what happens is in Application, which can be tested without a timer or a host.
/// </summary>
/// <remarks>
/// A hosted service is a singleton, and <see cref="ISender"/> is scoped. So the worker can't take
/// an ISender in its constructor: it takes a scope factory and opens a scope per unit of work.
/// </remarks>
public sealed class ShippingWorker(
    IServiceScopeFactory scopes,
    IOptions<ShippingOptions> options,
    TimeProvider time,
    ILogger<ShippingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // PeriodicTimer never overlaps: when an iteration takes longer than the interval, the next
        // one starts right after it.
        using var timer = new PeriodicTimer(options.Value.Interval, time);

        try
        {
            do
            {
                await RunOnce(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Stopping, no more orders will be shipped");
        }
    }

    /// <summary>One iteration: ships every order that is placed now.</summary>
    public async Task RunOnce(CancellationToken ct)
    {
        var placed = new List<Guid>();

        await InScope(async sender =>
        {
            var orders = await sender.CreateStream(new ListOrdersQuery(OrderStatus.Placed), ct);
            if (orders.IsFailure)
                return;

            await foreach (var order in orders.Value.WithCancellation(ct))
                placed.Add(order.Id);
        }, ct);

        foreach (var id in placed)
        {
            // LoggingBehavior logs a failed Result. The order then stays placed, and the next
            // iteration tries it again.
            await InScope(async sender => await sender.Send(new ShipOrderCommand(id), ct), ct);
        }
    }

    // A scope per unit of work: each one gets its own handlers and DbContext, and one that fails
    // leaves nothing behind for the next.
    private async Task InScope(Func<ISender, Task> work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await work(scope.ServiceProvider.GetRequiredService<ISender>());
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // An exception that leaves ExecuteAsync stops the whole host. Log it and carry on.
            logger.LogError(ex, "A unit of work failed unexpectedly");
        }
    }
}
