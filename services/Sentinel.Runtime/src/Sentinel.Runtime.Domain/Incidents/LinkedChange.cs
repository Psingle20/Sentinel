using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// A change correlated with an incident: one deployment or configuration change, with every
/// observation that reported it (started, completed or failed).
/// </summary>
public sealed record LinkedChange
{
    public required string DeploymentId { get; init; }

    public required ServiceId ServiceId { get; init; }

    public required string Version { get; init; }

    public string? PreviousVersion { get; init; }

    public required string ChangeKind { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the new version started serving; null while the change is in progress.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    public bool Failed { get; init; }

    public required IReadOnlyList<ObservationId> ObservationIds { get; init; }

    /// <summary>How strongly correlation linked the change to the incident (Phase 03), from 0 to 1.</summary>
    public double? CorrelationScore { get; init; }

    /// <summary>Why it was linked, for example "direct dependency of checkout-service; started 2.1 min before onset".</summary>
    public IReadOnlyList<string> CorrelationReasons { get; init; } = [];
}
