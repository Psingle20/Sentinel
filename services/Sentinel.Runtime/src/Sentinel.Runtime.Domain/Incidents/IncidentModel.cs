using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public enum IncidentState
{
    Detected,
    Investigating,
    AwaitingApproval,
    Remediating,
    Verifying,
    Resolved,
    Escalated
}

public enum IncidentSeverity
{
    Critical,
    High,
    Medium,
    Low
}

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

public enum EvidenceDirection
{
    Supports,
    Contradicts,
    Neutral
}

public sealed record Evidence
{
    public required EvidenceId EvidenceId { get; init; }

    public required string Statement { get; init; }

    public required EvidenceDirection Direction { get; init; }

    public required double Strength { get; init; }

    public required IReadOnlyList<ObservationId> SourceObservationIds { get; init; }

    public IReadOnlyList<HypothesisId> HypothesisIds { get; init; } = [];

    public required DateTimeOffset From { get; init; }

    public required DateTimeOffset To { get; init; }

    public required string ToolCallId { get; init; }

    public string? IndependenceGroup { get; init; }

    /// <summary>Derived from an incomplete, truncated or stale query result (ADR-007); weighted down by consensus.</summary>
    public bool Partial { get; init; }
}

public sealed record Hypothesis
{
    public required HypothesisId HypothesisId { get; init; }

    public required string Statement { get; init; }

    public required IReadOnlyList<string> CausalChain { get; init; }

    public ServiceId? SuspectedServiceId { get; init; }

    public ObservationId? SuspectedChangeObservationId { get; init; }

    public IReadOnlyList<EvidenceId> SupportingEvidenceIds { get; init; } = [];

    public IReadOnlyList<EvidenceId> ContradictingEvidenceIds { get; init; } = [];

    /// <summary>The explanation's identity on the blackboard; equal keys merge, and <c>a:b</c> refines <c>a</c>.</summary>
    public string? Key { get; init; }
}

public sealed record RejectedEvidence(EvidenceId EvidenceId, string Reason);

public enum InvestigationResult
{
    Completed,
    Abstained,
    Failed
}

public sealed record InvestigationReport
{
    public required InvestigationId InvestigationId { get; init; }

    public required AgentExecutionId AgentExecutionId { get; init; }

    public required InvestigationResult Result { get; init; }

    public IReadOnlyList<Evidence> Evidence { get; init; } = [];

    public IReadOnlyList<Hypothesis> Hypotheses { get; init; } = [];

    public string? AbstentionReason { get; init; }

    public required string ModelProvider { get; init; }

    public required string Model { get; init; }

    public string? ModelVersion { get; init; }

    public required string PromptVersion { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>The specialist role that produced the report; null is the generalist.</summary>
    public string? Role { get; init; }

    /// <summary>The verifier's challenges (Phase 05); empty for other roles.</summary>
    public IReadOnlyList<Challenge> Challenges { get; init; } = [];

    /// <summary>What the investigator could not determine, for the decision's missing-evidence record.</summary>
    public IReadOnlyList<string> Unknowns { get; init; } = [];

    /// <summary>The tool catalog the investigator could call, for the version manifest (Phase 07).</summary>
    public string? ToolCatalogVersion { get; init; }
}

public enum DecisionResult
{
    Diagnosed,
    Abstained
}

public sealed record CheckResult(string Name, bool Passed, string Detail);

public sealed record Decision
{
    public required DecisionId DecisionId { get; init; }

    public required DecisionResult Result { get; init; }

    public HypothesisId? LeadingHypothesisId { get; init; }

    public IReadOnlyList<HypothesisId> RejectedHypothesisIds { get; init; } = [];

    public required double Confidence { get; init; }

    public required double EvidenceCompleteness { get; init; }

    public required string Rationale { get; init; }

    public IReadOnlyList<CheckResult> Checks { get; init; } = [];

    /// <summary>Alternatives that stand within the dissent margin of the leader: disagreement preserved, not discarded.</summary>
    public IReadOnlyList<HypothesisId> DissentingHypothesisIds { get; init; } = [];

    public IReadOnlyList<HypothesisAssessment> Assessments { get; init; } = [];

    public string AlgorithmVersion { get; init; } = "falsification-v1";

    /// <summary>What the investigators could not determine and the decision lacked.</summary>
    public IReadOnlyList<string> MissingEvidence { get; init; } = [];

    /// <summary>Hash of the versioned inputs, so a replay can show it decided on the same evidence.</summary>
    public string? InputsHash { get; init; }

    /// <summary>Set when a later decision supersedes this one; the history is kept.</summary>
    public DecisionId? SupersededBy { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }

    /// <summary>The consensus settings the decision was made with, so a domain replay re-runs it exactly (Phase 07); null for falsification-only decisions and decisions recorded before Phase 07.</summary>
    public ConsensusSettings? Settings { get; init; }
}

/// <summary>
/// A remembered incident that resembled this one when an investigation was requested (Phase 07).
/// Kept in the request event so the investigation's inputs are part of the stream and replayable.
/// </summary>
public sealed record PriorIncident(IncidentId IncidentId, double Similarity, IReadOnlyList<string> SharedFeatures, string? CauseKey, string? CauseStatement, string Outcome, DateTimeOffset RecordedAt);

public enum PolicyVerdict
{
    Allow,
    Deny,
    ApprovalRequired,
    SuggestionOnly
}

/// <summary>
/// One immutable plan version. The full plan document is kept as the canonical contract JSON it
/// was hashed from, so approval, execution and audit all refer to the same bytes.
/// </summary>
public sealed record ProposedPlan
{
    public required PlanId PlanId { get; init; }

    public required int Version { get; init; }

    public required string Hash { get; init; }

    public required HypothesisId HypothesisId { get; init; }

    public required ServiceId TargetServiceId { get; init; }

    public required TimeSpan ObservationWindow { get; init; }

    public required string Document { get; init; }

    public required PolicyVerdict Policy { get; init; }

    public required string PolicyVersion { get; init; }

    public IReadOnlyList<string> PolicyTrace { get; init; } = [];

    public required DateTimeOffset ProposedAt { get; init; }

    /// <summary>The plan's actions in operator terms, for an escalation that suggests rather than executes.</summary>
    public string? Summary { get; init; }
}

public enum ExecutionResult
{
    Rejected,
    Aborted,
    ActionVerified,
    Failed,
    OutcomeUnknown,
    Compensated,
    CompensationFailed
}

public enum RecoveryResult
{
    Recovered,
    Unchanged,
    Degraded,
    Inconclusive,
    CompensationRequired
}

public sealed record Measurement(string ServiceId, string MetricName, string Comparison, double Threshold, double? ObservedValue, bool? Met);
