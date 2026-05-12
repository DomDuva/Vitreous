using System.Runtime.CompilerServices;
using Vitreous.Abstractions;

namespace Vitreous.Sample.Orders.Queries;

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Emits a fixed number of fake <see cref="OrderDto"/> objects at a rate of one
/// per second, then stops.  Useful for observing the Vitreous DevUI stream row
/// update in real time without needing any data in the repository.
/// </summary>
/// <param name="Count">
/// Number of items to emit.  Defaults to 5; clamped to [1, 20].
/// </param>
public sealed record TickStreamQuery(int Count = 5) : IStreamQuery<OrderDto>;

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Yields <see cref="TickStreamQuery.Count"/> synthetic orders spaced one second
/// apart so that the DevUI live timer and STREAMING status are clearly visible.
/// </summary>
public sealed class TickStreamQueryHandler(ILogger<TickStreamQueryHandler> logger)
    : StreamQueryHandler<TickStreamQuery, OrderDto>
{
    protected override async IAsyncEnumerable<OrderDto> Stream(
        TickStreamQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var count = Math.Clamp(query.Count, 1, 20);
        logger.LogInformation("Starting tick stream — will emit {Count} item(s) at 1-second intervals.", count);

        for (var i = 1; i <= count; i++)
        {
            ct.ThrowIfCancellationRequested();

            // One-second cadence so the live timer in the DevUI is clearly visible
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            logger.LogDebug("Emitting tick item {Index}/{Count}.", i, count);

            yield return new OrderDto(
                Id: Guid.NewGuid(),
                CustomerName: $"Sample Customer {i}",
                Product: $"Fake Product {i}",
                Quantity: i,
                CreatedAt: DateTimeOffset.UtcNow);
        }

        logger.LogInformation("Tick stream complete — emitted {Count} item(s).", count);
    }
}
