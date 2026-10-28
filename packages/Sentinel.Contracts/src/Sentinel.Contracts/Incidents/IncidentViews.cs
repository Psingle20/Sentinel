using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// Runtime's incident read model, served to Api over HTTP (ADR-012 bounded request/response).
/// Views are projections: rebuildable from the incident stream and never authoritative.
/// </summary>
public sealed record IncidentSummary
{
    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required ServiceId AffectedServiceId { get; init; }

    public required string Title { get; init; }

    public required Severity Severity { get; init; }

    public required IncidentStatus Status { get; init; }

    public required DateTimeOffset OpenedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required long StreamVersion { get; init; }

    /// <summary>When symptoms began in event time; the incident opened shortly after.</summary>
    public DateTimeOffset? SymptomOnset { get; init; }
}

public sealed record TimelineEntry(long StreamVersion, DateTimeOffset OccurredAt, string Kind, string Summary);

public enum EvidenceStatus
{
    Accepted,
    Rejected
}

public sealed record EvidenceView(EvidenceFinding Finding, InvestigationId InvestigationId, EvidenceStatus Status, string? RejectionReason);

public enum HypothesisStatus
{
    Proposed,
    Supported,
    Challenged,
    Rejected,
    Confirmed,
    Superseded
}

/// <summary>
/// A blackboard hypothesis: one explanation however many investigators proposed it. Lineage lists
/// the merged proposals; a refinement names the broader explanation it narrows.
/// </summary>
public sealed record HypothesisView(
    HypothesisFinding Finding,
    InvestigationId InvestigationId,
    HypothesisStatus Status,
    IReadOnlyList<HypothesisId>? Lineage = null,
    IReadOnlyList<InvestigationId>? ProposedBy = null,
    string? Refines = null);

public enum CausalNodeKind
{
    Symptom,
    Service,
    Change,
    Hypothesis,
    Evidence
}

public sealed record CausalNode(string Id, CausalNodeKind Kind, string Label);

/// <summary>An edge such as <c>supports</c>, <c>contradicts</c>, <c>blames</c>, <c>depends-on</c> or <c>affects</c>.</summary>
public sealed record CausalEdge(string From, string To, string Relation, double? Weight = null);

/// <summary>The incident's causal graph (Phase 04), derived from the stream: changes, symptoms, services, evidence and hypotheses.</summary>
public sealed record CausalGraphView(IReadOnlyList<CausalNode> Nodes, IReadOnlyList<CausalEdge> Edges);

/// <summary>One deterministic check Runtime applied before deciding, with its result.</summary>
public sealed record DecisionCheck(string Name, bool Passed, string Detail);

public sealed record DecisionView
{
    public required DecisionId DecisionId { get; init; }

    public required DecisionOutcome Outcome { get; init; }

    public HypothesisId? LeadingHypothesisId { get; init; }

    public IReadOnlyList<HypothesisId> RejectedHypothesisIds { get; init; } = [];

    public required double Confidence { get; init; }

    public required double EvidenceCompleteness { get; init; }

    public required Calibration Calibration { get; init; }

    public required string Rationale { get; init; }

    public IReadOnlyList<DecisionCheck> Checks { get; init; } = [];

    public required DateTimeOffset DecidedAt { get; init; }

    /// <summary>Alternatives within the dissent margin of the leader: disagreement kept, not discarded (Phase 05).</summary>
    public IReadOnlyList<HypothesisId> DissentingHypothesisIds { get; init; } = [];

    /// <summary>Why each hypothesis won or lost: its score, independent support and contradiction.</summary>
    public IReadOnlyList<HypothesisAssessmentView> Assessments { get; init; } = [];

    /// <summary>What the investigators could not determine.</summary>
    public IReadOnlyList<string> MissingEvidence { get; init; } = [];

    public string? AlgorithmVersion { get; init; }

    /// <summary>Hash of the versioned inputs, so a replay can show it decided on the same evidence.</summary>
    public string? InputsHash { get; init; }

    /// <summary>Set when a later decision superseded this one.</summary>
    public DecisionId? SupersededBy { get; init; }
}

public sealed record HypothesisAssessmentView(HypothesisId HypothesisId, double Score, int IndependentSupport, int IndependentContradiction, bool Eliminated, string Reason);

/// <summary>A change correlation linked to the incident, with its score and the reasons (Phase 03).</summary>
public sealed record LinkedChangeView(
    string DeploymentId,
    ServiceId ServiceId,
    string ChangeKind,
    string Version,
    string? PreviousVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset? ServingFrom,
    bool Failed,
    double? Score,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<ObservationId> ObservationIds);

/// <summary>A related service's symptom correlation merged into the incident.</summary>
public sealed record LinkedSignalView(ServiceId ServiceId, string MetricName, double Value, double Score, IReadOnlyList<string> Reasons, DateTimeOffset LinkedAt);

/// <summary>One adversarial challenge of a hypothesis and how it ended (Phase 05).</summary>
public sealed record ChallengeView(HypothesisId HypothesisId, string Falsifier, ChallengeVerdict Verdict, string Detail, IReadOnlyList<EvidenceId> EvidenceIds, InvestigationId InvestigationId);

public enum PlanStatus
{
    AwaitingApproval,
    SuggestionOnly,
    Denied,
    Approved,
    Rejected,
    Executing,
    Executed,
    Failed,
    Superseded
}

public sealed record PlanView(RemediationPlan Plan, PolicyEvaluation PolicyEvaluation, PlanStatus Status, string? ApprovedBy, DateTimeOffset? ApprovedAt);

public sealed record ExecutionView(PlanId PlanId, int PlanVersion, ExecutionOutcome? Outcome, string? Detail, DateTimeOffset RequestedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);

public sealed record RecoveryView(PlanId PlanId, RecoveryOutcome Outcome, IReadOnlyList<RecoveryMeasurement> Measurements, DateTimeOffset ObservedThrough, DateTimeOffset EvaluatedAt, string? Reason);

/// <summary>Which investigator ran, with which model, and how it ended.</summary>
public sealed record InvestigationView(
    InvestigationId InvestigationId,
    DateTimeOffset RequestedAt,
    AgentExecutionId? AgentExecutionId,
    InvestigationOutcome? Outcome,
    ModelMetadata? Model,
    string? AbstentionReason,
    DateTimeOffset? CompletedAt,
    string? Role = null,
    bool TimedOut = false);

public sealed record IncidentDetail
{
    public required IncidentSummary Summary { get; init; }

    public IReadOnlyList<ObservationId> TriggerObservationIds { get; init; } = [];

    public IReadOnlyList<ObservationId> LinkedDeploymentObservationIds { get; init; } = [];

    public IReadOnlyList<TimelineEntry> Timeline { get; init; } = [];

    public IReadOnlyList<InvestigationView> Investigations { get; init; } = [];

    public IReadOnlyList<EvidenceView> Evidence { get; init; } = [];

    public IReadOnlyList<HypothesisView> Hypotheses { get; init; } = [];

    public DecisionView? Decision { get; init; }

    public PlanView? Plan { get; init; }

    public ExecutionView? Execution { get; init; }

    public RecoveryView? Recovery { get; init; }

    /// <summary>The affected service and every related service whose symptom was merged in.</summary>
    public IReadOnlyList<ServiceId> AffectedServices { get; init; } = [];

    public IReadOnlyList<LinkedChangeView> Changes { get; init; } = [];

    public IReadOnlyList<LinkedSignalView> LinkedSignals { get; init; } = [];

    public IReadOnlyList<ChallengeView> Challenges { get; init; } = [];

    /// <summary>Every decision of the current cycle, oldest first, superseded ones included.</summary>
    public IReadOnlyList<DecisionView> Decisions { get; init; } = [];

    /// <summary>How many times the incident was reopened.</summary>
    public int Cycle { get; init; }

    public CausalGraphView? CausalGraph { get; init; }
}

/// <summary>
/// The incident's authoritative state from its writer beside the version its projection was built from,
/// so a caller can tell authoritative state from a lagging projection (Phase 03).
/// </summary>
public sealed record IncidentStateView(
    IncidentId IncidentId,
    IncidentStatus Status,
    long StreamVersion,
    long? ProjectionVersion,
    long? ProjectionLag,
    int Cycle,
    IReadOnlyList<InvestigationId> PendingInvestigationIds);

/// <summary>
/// One incident's complete record for evaluation: what a run produced, exported by Runtime
/// (Phase 01A) and read by the Phase 07 evaluation harness. Schema 2 adds the version manifest and
/// the incident-memory record; schema-1 records stay readable with both absent.
/// </summary>
public sealed record IncidentRunRecord(int SchemaVersion, IncidentDetail Incident, DateTimeOffset ExportedAt, VersionManifest? Versions = null, IncidentMemoryView? Memory = null)
{
    public const int CurrentSchemaVersion = 2;
}
