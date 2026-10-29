using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// Runtime opened an incident. <see cref="StreamVersion"/> is the incident stream version after
/// the change, so consumers can order updates and detect gaps (ADR-004).
/// </summary>
public sealed record IncidentOpened : ISentinelMessage
{
    public static string MessageType => "runtime.incident.opened";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Incidents;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required ServiceId AffectedServiceId { get; init; }

    public required string Title { get; init; }

    public required Severity Severity { get; init; }

    public required DateTimeOffset OpenedAt { get; init; }

    /// <summary>The observations that triggered detection, such as an SLO burn-rate sample.</summary>
    public required IReadOnlyList<ObservationId> TriggerObservationIds { get; init; }

    /// <summary>Recent deployments correlated with the incident.</summary>
    public IReadOnlyList<ObservationId> LinkedDeploymentObservationIds { get; init; } = [];

    public required long StreamVersion { get; init; }
}
