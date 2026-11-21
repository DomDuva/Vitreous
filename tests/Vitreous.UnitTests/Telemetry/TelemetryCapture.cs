using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Vitreous.UnitTests.Telemetry;

/// <summary>
/// Listens to the Vitreous activity source and meter, recording every span that ends and
/// every <c>vitreous.dispatch.duration</c> measurement.
/// </summary>
internal sealed class TelemetryCapture : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener = new();
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ConcurrentQueue<Measurement> _measurements = new();

    public TelemetryCapture(bool traces = true, bool metrics = true)
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo  = source => traces && source.Name == VitreousTelemetry.ActivitySourceName,
            Sample          = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (metrics && instrument.Meter.Name == VitreousTelemetry.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        // On .NET 8, MeterListener.Dispose only detaches from its instruments when this callback
        // is set, and the instruments would otherwise stay Enabled after the capture is disposed.
        _meterListener.MeasurementsCompleted = static (_, _) => { };
        _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            _measurements.Enqueue(new Measurement(instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        _meterListener.Start();
    }

    /// <summary>Spans that have ended, in the order they ended.</summary>
    public IReadOnlyList<Activity> Stopped => _stopped.ToArray();

    public IReadOnlyList<Measurement> Measurements => _measurements.ToArray();

    /// <summary>Ended spans whose message type is <typeparamref name="TMessage"/>.</summary>
    public IReadOnlyList<Activity> SpansFor<TMessage>() =>
        Stopped.Where(a => (string?)a.GetTagItem("vitreous.message.type") == typeof(TMessage).FullName).ToArray();

    /// <summary>Duration measurements whose message type is <typeparamref name="TMessage"/>.</summary>
    public IReadOnlyList<Measurement> MeasurementsFor<TMessage>() =>
        Measurements.Where(m => m.Tags.TryGetValue("vitreous.message.type", out var t) && (string?)t == typeof(TMessage).FullName).ToArray();

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    internal sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);
}

/// <summary>
/// Activity and meter listeners are process-wide, so telemetry tests run on their own:
/// another test's listener would make <see cref="DispatchTelemetry.IsEnabled"/> true.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryCollection
{
    public const string Name = "Telemetry";
}
