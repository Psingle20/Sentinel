using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Correlation;

/// <summary>
/// The walking skeleton's detection rule: an SLO burn signal above <see cref="DetectionThreshold"/>
/// for <see cref="RequiredSamples"/> consecutive samples opens an incident. Symptom onset is the
/// first sample of the run above <see cref="OnsetThreshold"/>, the rate at which the error budget
/// is spent faster than it accrues.
/// </summary>
public sealed record DetectionRule
{
    public required ServiceId ServiceId { get; init; }

    public required string MetricName { get; init; }

    public required double DetectionThreshold { get; init; }

    public double OnsetThreshold { get; init; } = 1.0;

    public int RequiredSamples { get; init; } = 2;

    /// <summary>How far before detection a change may start and still be linked to the incident.</summary>
    public TimeSpan Lookback { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long the signal must stay below onset before an incident left for humans is resolved as cleared.</summary>
    public TimeSpan ClearAfter { get; init; } = TimeSpan.FromMinutes(2);
}
