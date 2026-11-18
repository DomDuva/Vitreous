using Vitreous.DevUI;
using Vitreous.DevUI.Tracing;

namespace Vitreous.UnitTests.DevUI;

public sealed class TracingBehaviorGateTests
{
    private sealed record Ping : IQuery<int>;
    private sealed record Pinged : INotification;

    private static (TraceStore Store, DevUISerializer Serializer, DevUIState State) Parts()
    {
        var options = new VitreousDevUIOptions();
        return (new TraceStore(), new DevUISerializer(options), new DevUIState());
    }

    [Fact]
    public async Task Inactive_PassesThroughAndRecordsNothing()
    {
        var (store, serializer, state) = Parts();
        var behavior = new TracingBehavior<Ping, int>(store, serializer, state);

        var result = await behavior.Handle(new Ping(), (_, _) => new ValueTask<Result<int>>(42));
        await new NotificationTracingBehavior<Pinged>(store, serializer, state)
            .Handle(new Pinged(), (_, _) => ValueTask.CompletedTask);

        result.Value.Should().Be(42);
        store.Count.Should().Be(0);
    }

    [Fact]
    public async Task Active_RecordsATrace()
    {
        var (store, serializer, state) = Parts();
        state.IsActive = true;
        var behavior = new TracingBehavior<Ping, int>(store, serializer, state);

        await behavior.Handle(new Ping(), (_, _) => new ValueTask<Result<int>>(42));

        store.GetAll().Should().ContainSingle().Which.SerializedOutput.Should().Be("42");
    }
}
