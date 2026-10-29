using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Observations;

/// <summary>A dependency edge; <see cref="Observation.ServiceId"/> is the calling service.</summary>
public sealed record TopologyObservation : Observation
{
    public required ServiceId TargetServiceId { get; init; }

    public required string DependencyType { get; init; }

    public string? Protocol { get; init; }

    public required double Confidence { get; init; }

    public required DateTimeOffset FirstSeen { get; init; }

    public required DateTimeOffset LastSeen { get; init; }
}
