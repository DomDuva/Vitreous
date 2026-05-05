using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using Perfolizer.Metrology;

namespace Vitreous.Benchmarks.Benchmarks;

/// <summary>
/// Shared BenchmarkDotNet configuration applied to all benchmark classes.
///
/// Forces:
///   • Time unit  → nanoseconds (ns)  — readable for sub-microsecond dispatch
///   • Size unit  → bytes (B)         — exact allocation counts, no KB rounding
///
/// This matches the reference table format:
///   | Mediator        | Method                     | Mean (ns) | Allocated (B) |
/// </summary>
public sealed class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddJob(Job.Default);

        WithSummaryStyle(SummaryStyle.Default
            .WithTimeUnit(Perfolizer.Horology.TimeUnit.Nanosecond)
            .WithSizeUnit(SizeUnit.B));

        // Keep the same columns as the attribute-level [HideColumns(...)] but
        // apply them from config so both classes share one definition.
        HideColumns(
            Column.Error,
            Column.StdDev,
            Column.Median,
            Column.Ratio,
            Column.RatioSD,
            Column.Gen0,
            Column.Gen1,
            Column.Gen2,
            Column.AllocRatio);
    }
}
