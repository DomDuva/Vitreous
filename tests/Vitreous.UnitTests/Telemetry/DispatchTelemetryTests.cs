using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Vitreous.UnitTests.Telemetry;

/// <summary>
/// Spans and metrics recorded around dispatch through the reflection-based sender and the
/// notification publishers. The source-generated dispatcher is covered in
/// <c>Vitreous.SourceGenerationTests</c>.
/// </summary>
[Collection(TelemetryCollection.Name)]
public sealed class DispatchTelemetryTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    /// <summary>Captures <see cref="Activity.Current"/> where a handler runs.</summary>
    public sealed class Probe
    {
        public Activity? Current { get; set; }
    }

    // Value > 0 succeeds, value < 0 fails with a VALIDATION error, value == 0 throws.
    public sealed record TelCommand(int Value) : ICommand<int>;
    public sealed record TelVoidCommand : ICommand;
    public sealed record TelQuery(Probe Probe) : IQuery<string>;
    public sealed record TelCanceledQuery : IQuery<string>;
    public sealed record TelStream(int Count, bool Fault, Probe Probe) : IStreamQuery<int>;
    public sealed record TelRejectedStream : IStreamQuery<int>;
    public sealed record TelNotification : INotification;
    public sealed record TelFailingNotification : INotification;

    public sealed class TelCommandHandler : ICommandHandler<TelCommand, int>
    {
        public ValueTask<Result<int>> Handle(TelCommand command, CancellationToken ct = default) => command.Value switch
        {
            > 0 => new(command.Value * 2),
            < 0 => new(Error.Validation("Value must be positive.")),
            _   => throw new InvalidOperationException("boom"),
        };
    }

    public sealed class TelVoidCommandHandler : ICommandHandler<TelVoidCommand>
    {
        public ValueTask<Result> Handle(TelVoidCommand command, CancellationToken ct = default) => new(Result.Success);
    }

    public sealed class TelQueryHandler : IQueryHandler<TelQuery, string>
    {
        public async ValueTask<Result<string>> Handle(TelQuery query, CancellationToken ct = default)
        {
            await Task.Yield();
            query.Probe.Current = Activity.Current;
            return "ok";
        }
    }

    public sealed class TelCanceledQueryHandler : IQueryHandler<TelCanceledQuery, string>
    {
        public async ValueTask<Result<string>> Handle(TelCanceledQuery query, CancellationToken ct = default)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            return "not canceled";
        }
    }

    public sealed class TelStreamHandler : StreamQueryHandler<TelStream, int>
    {
        protected override async IAsyncEnumerable<int> Stream(TelStream query, [EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 1; i <= query.Count; i++)
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                query.Probe.Current = Activity.Current;
                yield return i;
            }

            if (query.Fault)
                throw new InvalidOperationException("stream broke");
        }
    }

    public sealed class TelRejectedStreamHandler : IStreamQueryHandler<TelRejectedStream, int>
    {
        public ValueTask<Result<IAsyncEnumerable<int>>> Handle(TelRejectedStream query, CancellationToken ct = default) =>
            new(Error.Unauthorized("No streams for you."));
    }

    public sealed class TelNotificationHandlerA : INotificationHandler<TelNotification>
    {
        public async ValueTask Handle(TelNotification notification, CancellationToken ct = default) => await Task.Yield();
    }

    public sealed class TelNotificationHandlerB : INotificationHandler<TelNotification>
    {
        public ValueTask Handle(TelNotification notification, CancellationToken ct = default) => default;
    }

    public sealed class TelFailingNotificationHandler : INotificationHandler<TelFailingNotification>
    {
        public ValueTask Handle(TelFailingNotification notification, CancellationToken ct = default) =>
            throw new InvalidOperationException("handler broke");
    }

    public sealed class TelOtherNotificationHandler : INotificationHandler<TelFailingNotification>
    {
        public ValueTask Handle(TelFailingNotification notification, CancellationToken ct = default) => default;
    }

    private static IServiceProvider CreateScope(NotificationPublishStrategy strategy = NotificationPublishStrategy.Sequential)
    {
        var services = new ServiceCollection();
        services.AddVitreous(o => o.NotificationPublishStrategy = strategy);
        services.AddSingleton<ICommandHandler<TelCommand, int>, TelCommandHandler>();
        services.AddSingleton<ICommandHandler<TelVoidCommand>, TelVoidCommandHandler>();
        services.AddSingleton<IQueryHandler<TelQuery, string>, TelQueryHandler>();
        services.AddSingleton<IQueryHandler<TelCanceledQuery, string>, TelCanceledQueryHandler>();
        services.AddSingleton<IStreamQueryHandler<TelStream, int>, TelStreamHandler>();
        services.AddSingleton<IStreamQueryHandler<TelRejectedStream, int>, TelRejectedStreamHandler>();
        services.AddSingleton<INotificationHandler<TelNotification>, TelNotificationHandlerA>();
        services.AddSingleton<INotificationHandler<TelNotification>, TelNotificationHandlerB>();
        services.AddSingleton<INotificationHandler<TelFailingNotification>, TelFailingNotificationHandler>();
        services.AddSingleton<INotificationHandler<TelFailingNotification>, TelOtherNotificationHandler>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider;
    }

    private static ISender Sender() => CreateScope().GetRequiredService<ISender>();

    // ── Commands and queries ──────────────────────────────────────────────────

    [Fact]
    public async Task Command_RecordsAnInternalSpanWithMessageTags()
    {
        using var capture = new TelemetryCapture();

        (await Sender().Send(new TelCommand(21))).Value.Should().Be(42);

        var span = capture.SpansFor<TelCommand>().Should().ContainSingle().Subject;
        span.Source.Name.Should().Be("Vitreous");
        span.DisplayName.Should().Be("vitreous command TelCommand");
        span.Kind.Should().Be(ActivityKind.Internal);
        span.Status.Should().Be(ActivityStatusCode.Unset);
        span.GetTagItem("vitreous.message.kind").Should().Be("command");
        span.GetTagItem("vitreous.dispatch.outcome").Should().Be("success");
        span.GetTagItem("error.type").Should().BeNull();
    }

    [Fact]
    public async Task VoidCommand_IsRecordedAsACommand()
    {
        using var capture = new TelemetryCapture();

        (await Sender().Send(new TelVoidCommand())).IsSuccess.Should().BeTrue();

        var span = capture.SpansFor<TelVoidCommand>().Should().ContainSingle().Subject;
        span.DisplayName.Should().Be("vitreous command TelVoidCommand");
        span.GetTagItem("vitreous.message.kind").Should().Be("command");
    }

    [Fact]
    public async Task Query_SpanIsCurrentInsideTheHandler_AndRestoredAfterwards()
    {
        using var capture = new TelemetryCapture();
        using var parent = new Activity("caller").Start();
        var probe = new Probe();

        (await Sender().Send(new TelQuery(probe))).Value.Should().Be("ok");

        var span = capture.SpansFor<TelQuery>().Should().ContainSingle().Subject;
        span.DisplayName.Should().Be("vitreous query TelQuery");
        span.GetTagItem("vitreous.message.kind").Should().Be("query");
        span.ParentSpanId.Should().Be(parent.SpanId);
        probe.Current.Should().BeSameAs(span, "spans started by the handler must be children of the dispatch span");
        Activity.Current.Should().BeSameAs(parent, "the dispatch span must not leak into the caller");
    }

    [Fact]
    public async Task FailedResult_MarksTheSpanAsAnErrorWithTheErrorCode()
    {
        using var capture = new TelemetryCapture();

        (await Sender().Send(new TelCommand(-1))).IsFailure.Should().BeTrue();

        var span = capture.SpansFor<TelCommand>().Should().ContainSingle().Subject;
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be("Value must be positive.");
        span.GetTagItem("error.type").Should().Be("VALIDATION");
        span.GetTagItem("vitreous.dispatch.outcome").Should().Be("failure");
    }

    [Fact]
    public async Task Exception_MarksTheSpanAsAnError_AddsAnExceptionEvent_AndPropagates()
    {
        using var capture = new TelemetryCapture();

        var act = async () => await Sender().Send(new TelCommand(0));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        var span = capture.SpansFor<TelCommand>().Should().ContainSingle().Subject;
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.GetTagItem("error.type").Should().Be(typeof(InvalidOperationException).FullName);
        span.GetTagItem("vitreous.dispatch.outcome").Should().Be("exception");
        var exception = span.Events.Should().ContainSingle(e => e.Name == "exception").Subject;
        exception.Tags.Should().Contain(new KeyValuePair<string, object?>("exception.type", typeof(InvalidOperationException).FullName));
        exception.Tags.Should().Contain(new KeyValuePair<string, object?>("exception.message", "boom"));
    }

    [Fact]
    public async Task CanceledDispatch_IsNotAnError()
    {
        using var capture = new TelemetryCapture();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await Sender().Send(new TelCanceledQuery(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var span = capture.SpansFor<TelCanceledQuery>().Should().ContainSingle().Subject;
        span.Status.Should().Be(ActivityStatusCode.Unset);
        span.GetTagItem("vitreous.dispatch.outcome").Should().Be("canceled");
        span.Events.Should().BeEmpty();
    }

    // ── Streams ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stream_SpanStaysOpenUntilEnumerated_AndCountsItems()
    {
        using var capture = new TelemetryCapture();
        var probe = new Probe();

        var stream = await Sender().CreateStream(new TelStream(3, Fault: false, probe));
        capture.SpansFor<TelStream>().Should().BeEmpty("the span ends when the stream does");

        var items = new List<int>();
        await foreach (var item in stream.Value)
            items.Add(item);

        items.Should().Equal(1, 2, 3);
        var span = capture.SpansFor<TelStream>().Should().ContainSingle().Subject;
        span.DisplayName.Should().Be("vitreous stream TelStream");
        span.GetTagItem("vitreous.message.kind").Should().Be("stream");
        span.GetTagItem("vitreous.stream.items").Should().Be(3L);
        span.GetTagItem("vitreous.dispatch.outcome").Should().Be("success");
        probe.Current.Should().BeSameAs(span, "the stream span is current while the iterator runs");
    }

    [Fact]
    public async Task Stream_EarlyBreak_EndsTheSpan()
    {
        using var capture = new TelemetryCapture();

        var stream = await Sender().CreateStream(new TelStream(10, Fault: false, new Probe()));
        await foreach (var _ in stream.Value)
            break;

        var span = capture.SpansFor<TelStream>().Should().ContainSingle().Subject;
        span.GetTagItem("vitreous.stream.items").Should().Be(1L);
        span.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task Stream_Fault_MarksTheSpanAsAnError()
    {
        using var capture = new TelemetryCapture();

        var stream = await Sender().CreateStream(new TelStream(2, Fault: true, new Probe()));
        var act = async () =>
        {
            await foreach (var _ in stream.Value) { }
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("stream broke");
        var span = capture.SpansFor<TelStream>().Should().ContainSingle().Subject;
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.GetTagItem("vitreous.stream.items").Should().Be(2L);
        span.GetTagItem("vitreous.dispatch.outcome").Should().Be("exception");
    }

    [Fact]
    public async Task Stream_Cancellation_IsNotAnError()
    {
        using var capture = new TelemetryCapture();
        using var cts = new CancellationTokenSource();

        var stream = await Sender().CreateStream(new TelStream(10, Fault: false, new Probe()));
        var act = async () =>
        {
            await foreach (var _ in stream.Value.WithCancellation(cts.Token))
                await cts.CancelAsync();
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        var span = capture.SpansFor<TelStream>().Should().ContainSingle().Subject;
        span.Status.Should().Be(ActivityStatusCode.Unset);
        span.GetTagItem("vitreous.dispatch.outcome").Should().Be("canceled");
    }

    [Fact]
    public async Task Stream_FailedResult_EndsTheSpanImmediately()
    {
        using var capture = new TelemetryCapture();

        (await Sender().CreateStream(new TelRejectedStream())).IsFailure.Should().BeTrue();

        var span = capture.SpansFor<TelRejectedStream>().Should().ContainSingle().Subject;
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.GetTagItem("error.type").Should().Be("UNAUTHORIZED");
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(NotificationPublishStrategy.Sequential)]
    [InlineData(NotificationPublishStrategy.Parallel)]
    public async Task Publish_RecordsAParentSpanWithAChildSpanPerHandler(NotificationPublishStrategy strategy)
    {
        using var capture = new TelemetryCapture();

        await CreateScope(strategy).GetRequiredService<IPublisher>().Publish(new TelNotification());

        var spans  = capture.SpansFor<TelNotification>();
        var parent = spans.Should().ContainSingle(s => s.DisplayName == "vitreous publish TelNotification").Subject;
        parent.GetTagItem("vitreous.message.kind").Should().Be("notification");
        parent.GetTagItem("vitreous.dispatch.outcome").Should().Be("success");

        var children = spans.Where(s => s != parent).ToArray();
        children.Select(c => c.DisplayName).Should().BeEquivalentTo(
            "vitreous handle TelNotificationHandlerA", "vitreous handle TelNotificationHandlerB");
        children.Should().AllSatisfy(c => c.ParentSpanId.Should().Be(parent.SpanId));
        children.Select(c => (string?)c.GetTagItem("vitreous.handler.type")).Should().BeEquivalentTo(
            typeof(TelNotificationHandlerA).FullName, typeof(TelNotificationHandlerB).FullName);
    }

    [Theory]
    [InlineData(NotificationPublishStrategy.Sequential)]
    [InlineData(NotificationPublishStrategy.Parallel)]
    public async Task Publish_FailingHandler_MarksItsSpanAndTheParent(NotificationPublishStrategy strategy)
    {
        using var capture = new TelemetryCapture();

        var act = async () => await CreateScope(strategy).GetRequiredService<IPublisher>().Publish(new TelFailingNotification());

        await act.Should().ThrowAsync<Exception>();
        var spans  = capture.SpansFor<TelFailingNotification>();
        var parent = spans.Should().ContainSingle(s => s.DisplayName == "vitreous publish TelFailingNotification").Subject;
        parent.Status.Should().Be(ActivityStatusCode.Error);
        spans.Should().ContainSingle(s => s.DisplayName == "vitreous handle TelFailingNotificationHandler")
            .Which.Status.Should().Be(ActivityStatusCode.Error);
    }

    // ── Metrics ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Duration_IsRecordedWithKindTypeAndOutcome()
    {
        using var capture = new TelemetryCapture(traces: false);

        await Sender().Send(new TelCommand(1));
        await Sender().Send(new TelCommand(-1));
        await CreateScope().GetRequiredService<IPublisher>().Publish(new TelNotification());

        var commands = capture.MeasurementsFor<TelCommand>();
        commands.Should().HaveCount(2).And.AllSatisfy(m =>
        {
            m.Instrument.Should().Be("vitreous.dispatch.duration");
            m.Value.Should().BeGreaterThanOrEqualTo(0);
            m.Tags["vitreous.message.kind"].Should().Be("command");
        });
        commands.Select(m => m.Tags["vitreous.dispatch.outcome"]).Should().BeEquivalentTo(["success", "failure"]);
        commands.Single(m => (string?)m.Tags["vitreous.dispatch.outcome"] == "failure").Tags["error.type"].Should().Be("VALIDATION");

        capture.MeasurementsFor<TelNotification>().Should().ContainSingle()
            .Which.Tags["vitreous.message.kind"].Should().Be("notification");
    }

    [Fact]
    public async Task NoListener_IsDisabled_AndDispatchStillWorks()
    {
        DispatchTelemetry.IsEnabled.Should().BeFalse();

        (await Sender().Send(new TelCommand(2))).Value.Should().Be(4);
        await CreateScope(NotificationPublishStrategy.Parallel).GetRequiredService<IPublisher>().Publish(new TelNotification());
        var stream = await Sender().CreateStream(new TelStream(2, Fault: false, new Probe()));
        stream.Value.GetType().Name.Should().NotStartWith("TelemetryAsyncEnumerable", "nothing is wrapped when nobody listens");
    }

    [Fact]
    public void Constants_MatchTheSourceAndMeterNames()
    {
        VitreousTelemetry.ActivitySourceName.Should().Be("Vitreous");
        VitreousTelemetry.MeterName.Should().Be("Vitreous");
    }
}
