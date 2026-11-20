using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Vitreous;

/// <summary>
/// The names of the <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> that Vitreous reports dispatches to.
/// Subscribe to them to see commands, queries, streams and notifications in OpenTelemetry:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(VitreousTelemetry.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(VitreousTelemetry.MeterName));
/// </code>
/// </summary>
/// <remarks>
/// Nothing is recorded until a listener subscribes. With no listener, a dispatch takes the
/// same path, and allocates the same, as it would with telemetry compiled out.
/// </remarks>
public static class VitreousTelemetry
{
    /// <summary>
    /// The name of the <see cref="System.Diagnostics.ActivitySource"/> that dispatch spans
    /// are started from: <c>"Vitreous"</c>.
    /// </summary>
    public const string ActivitySourceName = "Vitreous";

    /// <summary>
    /// The name of the <see cref="System.Diagnostics.Metrics.Meter"/> that dispatch metrics
    /// are recorded on: <c>"Vitreous"</c>.
    /// </summary>
    public const string MeterName = "Vitreous";

    internal static readonly string? Version = GetVersion(typeof(VitreousTelemetry).Assembly);

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    internal static readonly Meter Meter = new(MeterName, Version);

    // MinVer stamps "0.4.0+<commit>"; the source revision is noise in a telemetry scope version.
    private static string? GetVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { } version
            ? version.Split('+')[0]
            : assembly.GetName().Version?.ToString();
}
