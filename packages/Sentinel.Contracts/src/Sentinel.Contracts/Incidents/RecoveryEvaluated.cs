using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// Runtime evaluated recovery from observations. Inconclusive unless the observation window
/// was fully observed with complete data (ADR-007).
/// </summary>
public sealed record RecoveryEvaluated : ISentinelMessage
{
    public static string MessageType => "runtime.recovery.evaluated";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Incidents;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required RecoveryOutcome Outcome { get; init; }

    public required IReadOnlyList<RecoveryMeasurement> Measurements { get; init; }

    public required DateTimeOffset ObservedThrough { get; init; }

    public required DateTimeOffset EvaluatedAt { get; init; }

    public required long StreamVersion { get; init; }
}
