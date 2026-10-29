using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>One expected signal: for example checkout's SLO burn rate below 1.0.</summary>
public sealed record RecoveryCriterion
{
    public required ServiceId ServiceId { get; init; }

    public required string MetricName { get; init; }

    public required ThresholdComparison Comparison { get; init; }

    public required double Threshold { get; init; }
}
