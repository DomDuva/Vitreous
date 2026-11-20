using System.Diagnostics;
using System.Diagnostics.Metrics;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Vitreous;
using VH = Vitreous.Benchmarks.VitreousHandlers;
using VSender    = Vitreous.Abstractions.ISender;
using VPublisher = Vitreous.Abstractions.IPublisher;

namespace Vitreous.Benchmarks.Benchmarks;

/// <summary>Whether anything listens to the Vitreous activity source and meter.</summary>
public enum TelemetryListener
{
    /// <summary>Telemetry is compiled in, but nothing listens: the cost every app pays.</summary>
    None,

    /// <summary>
    /// An <see cref="ActivityListener"/> that records every span and a <see cref="MeterListener"/>
    /// that takes every measurement, as an OpenTelemetry SDK with Vitreous enabled would.
    /// </summary>
    Attached,
}

/// <summary>
/// The cost of the OpenTelemetry instrumentation on the source-generated dispatcher.
/// With <see cref="TelemetryListener.None"/> the numbers must match <see cref="DispatchBenchmarks"/>
/// (0 B for a plain send). BenchmarkDotNet runs each case in its own process, so the
/// listeners of the <see cref="TelemetryListener.Attached"/> case never reach the other one.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class TelemetryBenchmarks
{
    [Params(TelemetryListener.None, TelemetryListener.Attached)]
    public TelemetryListener Listener { get; set; }

    private VSender    _sender    = null!;
    private VPublisher _publisher = null!;
    private ActivityListener? _activityListener;
    private MeterListener? _meterListener;

    private static readonly VH.BenchQuery        VQ    = new();
    private static readonly VH.BenchVoidCommand  VVC   = new();
    private static readonly VH.BenchNotification VN    = new();
    private static readonly VH.BenchStreamQuery  VSQ10 = new(10);

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddVitreousHandlers(o => o.DefaultHandlerLifetime = ServiceLifetime.Singleton);
        var provider = services.BuildServiceProvider();
        _sender    = provider.GetRequiredService<VSender>();
        _publisher = provider.GetRequiredService<VPublisher>();

        if (Listener != TelemetryListener.Attached)
            return;

        _activityListener = new ActivityListener
        {
            ShouldListenTo = static s => s.Name == VitreousTelemetry.ActivitySourceName,
            Sample         = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == VitreousTelemetry.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        _meterListener.SetMeasurementEventCallback<double>(static (_, _, _, _) => { });
        _meterListener.Start();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _activityListener?.Dispose();
        _meterListener?.Dispose();
    }

    [Benchmark(Description = "Send (return result)")]
    public async Task Send_Result() => _ = await _sender.Send(VQ);

    [Benchmark(Description = "Send (return void)")]
    public async Task Send_Void() => await _sender.Send(VVC);

    [Benchmark(Description = "Publish")]
    public async Task Publish() => await _publisher.Publish(VN);

    [Benchmark(Description = "Stream query (10 items)")]
    public async Task Stream_10Items()
    {
        await foreach (var _ in (await _sender.CreateStream(VSQ10)).Value) { }
    }
}
