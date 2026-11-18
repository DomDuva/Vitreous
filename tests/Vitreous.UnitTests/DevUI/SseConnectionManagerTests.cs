using System.Collections;
using Vitreous.DevUI;
using Vitreous.DevUI.Middleware;
using Vitreous.DevUI.Tracing;

namespace Vitreous.UnitTests.DevUI;

public sealed class SseConnectionManagerTests
{
    private static SseConnectionManager Create(int capacity = 16)
    {
        var options = new VitreousDevUIOptions { TraceBufferCapacity = capacity };
        return new SseConnectionManager(new DevUISerializer(options), options);
    }

    private static ExecutionTrace Trace(string name = "T") => new() { MessageType = name };

    [Fact]
    public void Broadcast_WithNoClients_DoesNotSerialize()
    {
        using var sse = Create();

        // Serializing this trace throws; with nobody connected it must never be serialized.
        var act = () => sse.Broadcast(new ExecutionTrace { Logs = new ThrowingList() });

        act.Should().NotThrow();
    }

    [Fact]
    public async Task SlowClient_IsDropped_WhenItsBufferIsFull()
    {
        using var sse = Create(capacity: 16);
        using var slow = sse.Subscribe();

        for (var i = 0; i < 17; i++)
            sse.Broadcast(Trace());

        sse.ClientCount.Should().Be(0);
        var drained = 0;
        await foreach (var _ in slow.Reader.ReadAllAsync())
            drained++;
        drained.Should().Be(16, "buffered events are still delivered before the connection ends");
    }

    [Fact]
    public async Task ConcurrentBroadcasts_AreAllDelivered()
    {
        using var sse = Create(capacity: 10_000);
        using var client = sse.Subscribe();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
                sse.Broadcast(Trace());
        })));

        var received = 0;
        while (client.Reader.TryRead(out _))
            received++;
        received.Should().Be(4_000);
    }

    [Fact]
    public void DisposingASubscription_RemovesTheClient()
    {
        using var sse = Create();
        var subscription = sse.Subscribe();
        sse.ClientCount.Should().Be(1);

        subscription.Dispose();

        sse.ClientCount.Should().Be(0);
    }

    private sealed class ThrowingList : IReadOnlyList<TraceLogEntry>
    {
        public TraceLogEntry this[int index] => throw new InvalidOperationException("serialized");
        public int Count => throw new InvalidOperationException("serialized");
        public IEnumerator<TraceLogEntry> GetEnumerator() => throw new InvalidOperationException("serialized");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
