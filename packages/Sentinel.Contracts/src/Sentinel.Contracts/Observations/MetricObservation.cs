using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Observations;

public sealed record MetricObservation : Observation
{
    public required string MetricName { get; init; }

    public required double Value { get; init; }

    public string? Unit { get; init; }

    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    /// <summary>How the value was aggregated, for example <c>rate</c> or <c>p99</c>.</summary>
    public string? Aggregation { get; init; }

    public TimeSpan? Window { get; init; }
}
