using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Vitreous.Caching;

namespace Vitreous.CachingTests;

/// <summary>
/// The <c>vitreous.cache.requests</c> and <c>vitreous.cache.invalidations</c> counters on the
/// <see cref="VitreousCachingTelemetry.MeterName"/> meter.
/// </summary>
public sealed class CachingTelemetryTests
{
    private sealed record TelCachedQuery(int Id) : ICacheableQuery<string>
    {
        public string CacheKey              => $"tel:{Id}";
        public TimeSpan? AbsoluteExpiration => null;
        public TimeSpan? SlidingExpiration  => null;
    }

    private sealed record TelInvalidatingCommand : ICommand<bool>, IInvalidatesCache
    {
        public IReadOnlyList<string> CacheKeys       => ["tel:1", "tel:2"];
        public IReadOnlyList<string> InvalidatesTags => ["tel"];
    }

    /// <summary>Records the counters' measurements for <typeparamref name="TMessage"/> only.</summary>
    private sealed class CounterCapture<TMessage> : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Instrument, IReadOnlyDictionary<string, object?> Tags)> _measurements = new();

        public CounterCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == VitreousCachingTelemetry.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            // On .NET 8, MeterListener.Dispose only detaches from its instruments when this is set.
            _listener.MeasurementsCompleted = static (_, _) => { };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var map = tags.ToArray().ToDictionary(t => t.Key, t => t.Value);
                if ((string?)map["vitreous.message.type"] == typeof(TMessage).FullName)
                {
                    for (var i = 0; i < value; i++)
                        _measurements.Enqueue((instrument.Name, map));
                }
            });
            _listener.Start();
        }

        public IReadOnlyList<string?> Values(string instrument, string tag) =>
            _measurements.Where(m => m.Instrument == instrument).Select(m => (string?)m.Tags[tag]).ToArray();

        public void Dispose() => _listener.Dispose();
    }

    private static CachingBehavior<TelCachedQuery, string> CreateBehavior(FakeVitreousCache cache) =>
        new(cache, NullLogger<CachingBehavior<TelCachedQuery, string>>.Instance);

    [Fact]
    public async Task Requests_CountsHitsAndMisses()
    {
        using var capture = new CounterCapture<TelCachedQuery>();
        var cache    = new FakeVitreousCache();
        var behavior = CreateBehavior(cache);
        MessageHandlerDelegate<TelCachedQuery, string> handler = (_, _) => new("loaded");

        await behavior.Handle(new TelCachedQuery(1), handler, default);   // miss, stored
        await behavior.Handle(new TelCachedQuery(1), handler, default);   // hit

        capture.Values("vitreous.cache.requests", "vitreous.cache.result").Should().Equal("miss", "hit");
    }

    [Fact]
    public async Task Requests_ConcurrentMisses_CountOneMissAndTheRestCoalesced()
    {
        using var capture = new CounterCapture<TelCachedQuery>();
        var behavior = CreateBehavior(new FakeVitreousCache());
        var gate     = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        MessageHandlerDelegate<TelCachedQuery, string> slow = async (_, _) =>
        {
            await gate.Task;
            return "loaded";
        };

        var pending = Enumerable.Range(0, 3)
            .Select(_ => behavior.Handle(new TelCachedQuery(2), slow, default).AsTask())
            .ToList();
        gate.SetResult();
        await Task.WhenAll(pending);

        capture.Values("vitreous.cache.requests", "vitreous.cache.result")
            .Should().BeEquivalentTo(["miss", "coalesced", "coalesced"]);
    }

    [Fact]
    public async Task Hit_TagsTheCurrentVitreousSpan_ButNotOtherSpans()
    {
        using var vitreous = new ActivitySource(VitreousTelemetry.ActivitySourceName);
        using var other    = new ActivitySource("Not.Vitreous");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s == vitreous || s == other,
            Sample         = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var cache = new FakeVitreousCache();
        cache.Seed("tel:3", "cached");
        var behavior = CreateBehavior(cache);
        MessageHandlerDelegate<TelCachedQuery, string> handler = (_, _) => new("loaded");

        using (var span = vitreous.StartActivity("vitreous query TelCachedQuery")!)
        {
            await behavior.Handle(new TelCachedQuery(3), handler, default);
            span.GetTagItem("vitreous.cache.hit").Should().Be(true);
        }

        using (var span = other.StartActivity("someone else's span")!)
        {
            await behavior.Handle(new TelCachedQuery(3), handler, default);
            span.GetTagItem("vitreous.cache.hit").Should().BeNull();
        }
    }

    [Fact]
    public async Task Invalidations_CountsEachRemovedKeyAndTag()
    {
        using var capture = new CounterCapture<TelInvalidatingCommand>();
        var behavior = new CacheInvalidationBehavior<TelInvalidatingCommand, bool>(
            new FakeVitreousCache(), NullLogger<CacheInvalidationBehavior<TelInvalidatingCommand, bool>>.Instance);

        await behavior.Handle(new TelInvalidatingCommand(), (_, _) => new(true), default);

        capture.Values("vitreous.cache.invalidations", "vitreous.cache.invalidation.kind")
            .Should().Equal("key", "key", "tag");
    }

    [Fact]
    public void MeterName_IsVitreousCaching() =>
        VitreousCachingTelemetry.MeterName.Should().Be("Vitreous.Caching");
}
