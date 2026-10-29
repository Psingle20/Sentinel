namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// What was read for one expected recovery signal over the window: its samples, its average over an
/// equal window before the action, and, when the telemetry does not cover the window completely, why.
/// </summary>
public sealed record SignalReading(
    string ServiceId,
    string MetricName,
    RecoveryComparison Comparison,
    double Threshold,
    IReadOnlyList<MetricSample> Samples,
    double? Baseline,
    string? CoverageProblem);
