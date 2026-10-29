using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

public enum IncidentStatus
{
    Detected,
    Investigating,
    AwaitingApproval,
    Remediating,
    Verifying,
    Resolved,
    Escalated
}

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

public sealed record IncidentStatusChanged : ISentinelMessage
{
    public static string MessageType => "runtime.incident.status-changed";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Incidents;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required IncidentStatus Status { get; init; }

    public string? Reason { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }

    public required long StreamVersion { get; init; }
}

/// <summary>
/// Runtime accepted an investigation's findings onto the incident's evidence blackboard (Phase 04).
/// A notification for live views: it carries counts and keys, and the detail is read from Runtime.
/// </summary>
public sealed record IncidentBlackboardUpdated : ISentinelMessage
{
    public static string MessageType => "runtime.incident.blackboard-updated";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Incidents;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required InvestigationId InvestigationId { get; init; }

    public string? Role { get; init; }

    public required int AcceptedEvidence { get; init; }

    public required int RejectedEvidence { get; init; }

    /// <summary>The blackboard's hypothesis keys after the merge.</summary>
    public IReadOnlyList<string> HypothesisKeys { get; init; } = [];

    public required DateTimeOffset UpdatedAt { get; init; }

    public required long StreamVersion { get; init; }
}

public enum DecisionOutcome
{
    Diagnosed,
    Abstained
}

/// <summary>
/// Runtime recorded an evidence-backed decision. Confidence is always published with its
/// evidence completeness and calibration (Phase 05).
/// </summary>
public sealed record DecisionRecorded : ISentinelMessage
{
    public static string MessageType => "runtime.decision.recorded";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Incidents;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required DecisionId DecisionId { get; init; }

    public required DecisionOutcome Outcome { get; init; }

    public HypothesisId? LeadingHypothesisId { get; init; }

    public IReadOnlyList<HypothesisId> RejectedHypothesisIds { get; init; } = [];

    public required double Confidence { get; init; }

    public required double EvidenceCompleteness { get; init; }

    public required Calibration Calibration { get; init; }

    public required string Rationale { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }

    public required long StreamVersion { get; init; }
}

public enum RecoveryOutcome
{
    Recovered,
    Unchanged,
    Degraded,
    Inconclusive,
    CompensationRequired
}

/// <summary>One expected signal: for example checkout's SLO burn rate below 1.0.</summary>
public sealed record RecoveryCriterion
{
    public required ServiceId ServiceId { get; init; }

    public required string MetricName { get; init; }

    public required ThresholdComparison Comparison { get; init; }

    public required double Threshold { get; init; }
}

public enum ThresholdComparison
{
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}

/// <summary>A recovery signal after the action, and its pre-action baseline (Phase 08 before-and-after view; absent before it).</summary>
public sealed record RecoveryMeasurement(RecoveryCriterion Criterion, double? ObservedValue, bool? Met, double? BaselineValue = null);

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
