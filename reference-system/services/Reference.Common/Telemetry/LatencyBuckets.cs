using System.Diagnostics.Metrics;

namespace Reference.Common.Telemetry;

/// <summary>
/// Bucket boundaries for latency histograms recorded in seconds. Without them the SDK uses its
/// millisecond defaults, every sample lands in the first bucket and percentiles are meaningless.
/// </summary>
public static class LatencyBuckets
{
    public static InstrumentAdvice<double> Seconds { get; } = new()
    {
        HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.2, 0.3, 0.4, 0.5, 0.6, 0.75, 1, 1.5, 2, 2.5, 5, 10]
    };
}
