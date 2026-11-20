using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Vitreous.Caching;

/// <summary>
/// The name of the <see cref="System.Diagnostics.Metrics.Meter"/> that the caching behaviors
/// record on. It is separate from <see cref="VitreousTelemetry.MeterName"/> so caching metrics
/// can be enabled on their own:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithMetrics(m => m.AddMeter(VitreousTelemetry.MeterName, VitreousCachingTelemetry.MeterName));
/// </code>
/// </summary>
public static class VitreousCachingTelemetry
{
    /// <summary>
    /// The name of the <see cref="System.Diagnostics.Metrics.Meter"/> that cache requests and
    /// invalidations are counted on: <c>"Vitreous.Caching"</c>.
    /// </summary>
    public const string MeterName = "Vitreous.Caching";

    internal const string MessageTypeTag      = "vitreous.message.type";
    internal const string CacheResultTag      = "vitreous.cache.result";
    internal const string InvalidationKindTag = "vitreous.cache.invalidation.kind";
    internal const string CacheHitTag         = "vitreous.cache.hit";

    internal static readonly Meter Meter = new(MeterName, GetVersion(typeof(VitreousCachingTelemetry).Assembly));

    internal static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "vitreous.cache.requests",
        unit: "{request}",
        description: "Cacheable queries handled by CachingBehavior, by result: hit, miss (the handler ran) or coalesced (shared another caller's handler run).");

    internal static readonly Counter<long> Invalidations = Meter.CreateCounter<long>(
        "vitreous.cache.invalidations",
        unit: "{entry}",
        description: "Cache keys and tags removed by CacheInvalidationBehavior after a successful command.");

    internal static void RecordRequest(string result, string? messageType)
    {
        if (Requests.Enabled)
            Requests.Add(1, new KeyValuePair<string, object?>(CacheResultTag, result), new KeyValuePair<string, object?>(MessageTypeTag, messageType));

        // Annotate the dispatch span the behavior runs in, but never someone else's span.
        if (Activity.Current is { Source.Name: VitreousTelemetry.ActivitySourceName, IsAllDataRequested: true } activity)
            activity.SetTag(CacheHitTag, result == "hit");
    }

    internal static void RecordInvalidation(string kind, string? messageType)
    {
        if (Invalidations.Enabled)
            Invalidations.Add(1, new KeyValuePair<string, object?>(InvalidationKindTag, kind), new KeyValuePair<string, object?>(MessageTypeTag, messageType));
    }

    // MinVer stamps "0.4.0+<commit>"; the source revision is noise in a telemetry scope version.
    private static string? GetVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { } version
            ? version.Split('+')[0]
            : assembly.GetName().Version?.ToString();
}
