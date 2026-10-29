using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Correlation;

/// <summary>Runtime's running view of one signal for one service, advanced sample by sample in event time.</summary>
public sealed record SignalStreak
{
    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required ServiceId ServiceId { get; init; }

    public required string MetricName { get; init; }

    public DateTimeOffset? LastSampleAt { get; init; }

    public double LastValue { get; init; }

    public DateTimeOffset? OnsetAt { get; init; }

    public int ConsecutiveAboveDetection { get; init; }

    public DateTimeOffset? ClearSince { get; init; }

    /// <summary>Applies a sample. Samples at or before the last one are duplicates or late, and change nothing.</summary>
    public SignalStreak Observe(DateTimeOffset eventTime, double value, DetectionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (LastSampleAt is { } last && eventTime <= last)
        {
            return this;
        }

        bool aboveOnset = value > rule.OnsetThreshold;
        return this with
        {
            LastSampleAt = eventTime,
            LastValue = value,
            OnsetAt = aboveOnset ? OnsetAt ?? eventTime : null,
            ConsecutiveAboveDetection = value >= rule.DetectionThreshold ? ConsecutiveAboveDetection + 1 : 0,
            ClearSince = aboveOnset ? null : ClearSince ?? eventTime
        };
    }

    public bool Fires(DetectionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return ConsecutiveAboveDetection >= rule.RequiredSamples;
    }

    public bool HasCleared(DetectionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return ClearSince is { } since && LastSampleAt is { } last && last - since >= rule.ClearAfter;
    }
}
