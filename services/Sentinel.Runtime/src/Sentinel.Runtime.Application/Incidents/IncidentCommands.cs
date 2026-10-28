using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents;

/// <summary>
/// A command to the incident's single writer (ADR-004). Commands are plain, serializable data: any
/// I/O they depend on, such as a policy evaluation or a recovery measurement, happens before the
/// command is sent, so the writer only applies invariants and appends events. Every command is
/// idempotent, so a retried workflow step or redelivered message appends nothing new.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(OpenIncidentCommand), "open")]
[JsonDerivedType(typeof(RequestInvestigationsCommand), "request-investigations")]
[JsonDerivedType(typeof(RecordInvestigationCommand), "record-investigation")]
[JsonDerivedType(typeof(TimeOutInvestigationsCommand), "time-out-investigations")]
[JsonDerivedType(typeof(UpdateChangesCommand), "update-changes")]
[JsonDerivedType(typeof(RecordDecisionCommand), "record-decision")]
[JsonDerivedType(typeof(DecideCommand), "decide")]
[JsonDerivedType(typeof(ProposePlanCommand), "propose-plan")]
[JsonDerivedType(typeof(EscalateCommand), "escalate")]
[JsonDerivedType(typeof(ApprovePlanCommand), "approve-plan")]
[JsonDerivedType(typeof(RejectPlanCommand), "reject-plan")]
[JsonDerivedType(typeof(RevokeApprovalCommand), "revoke-approval")]
[JsonDerivedType(typeof(RequestExecutionCommand), "request-execution")]
[JsonDerivedType(typeof(ConfirmStartCommand), "confirm-start")]
[JsonDerivedType(typeof(RecordExecutionCommand), "record-execution")]
[JsonDerivedType(typeof(ScheduleRecoveryCommand), "schedule-recovery")]
[JsonDerivedType(typeof(RecordRecoveryCommand), "record-recovery")]
[JsonDerivedType(typeof(ResolveClearedCommand), "resolve-cleared")]
[JsonDerivedType(typeof(LinkSignalCommand), "link-signal")]
[JsonDerivedType(typeof(ChangeSeverityCommand), "change-severity")]
[JsonDerivedType(typeof(ReopenCommand), "reopen")]
public abstract record IncidentCommand;

public sealed record OpenIncidentCommand(
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    ServiceId ServiceId,
    string Title,
    IncidentSeverity Severity,
    DateTimeOffset SymptomOnset,
    IReadOnlyList<ObservationId> Triggers,
    IReadOnlyList<LinkedChange> Changes,
    string? TriggerMetric = null) : IncidentCommand;

public sealed record InvestigationSpec(
    InvestigationId InvestigationId,
    string? Role,
    string Objective,
    DateTimeOffset WindowFrom,
    DateTimeOffset WindowTo,
    IReadOnlyList<HypothesisId>? ChallengedHypothesisIds = null,
    IReadOnlyList<PriorIncident>? SimilarIncidents = null);

public sealed record RequestInvestigationsCommand(IReadOnlyList<InvestigationSpec> Investigations) : IncidentCommand;

public sealed record RecordInvestigationCommand(InvestigationReport Report) : IncidentCommand;

public sealed record TimeOutInvestigationsCommand(IReadOnlyList<InvestigationId> InvestigationIds) : IncidentCommand;

public sealed record UpdateChangesCommand(IReadOnlyList<LinkedChange> Changes, bool LinkNew) : IncidentCommand;

/// <summary>Records a decision; when <see cref="Supersede"/> is set, a later decision replaces the current one.</summary>
public sealed record RecordDecisionCommand(Decision Decision, string? Supersede = null) : IncidentCommand;

/// <summary>
/// Decides from the incident's own evidence, inside the writer's turn, so the decision is a pure
/// function of the stream and the versioned settings: consensus with the verifier's challenges
/// (Phase 05), or the walking skeleton's falsification check. Repeating it changes nothing.
/// </summary>
public sealed record DecideCommand(DecisionId DecisionId, bool UseConsensus, ConsensusSettings Settings) : IncidentCommand;

public sealed record ProposePlanCommand(ProposedPlan Plan) : IncidentCommand;

public sealed record EscalateCommand(string Reason, bool WithoutPlan = false) : IncidentCommand;

public sealed record ApprovePlanCommand(PlanId PlanId, int PlanVersion, string PlanHash, string ApproverId, string Attestation) : IncidentCommand;

public sealed record RejectPlanCommand(PlanId PlanId, int PlanVersion, string RejectedBy, string Reason) : IncidentCommand;

public sealed record RevokeApprovalCommand(PlanId PlanId, int PlanVersion, string RevokedBy, string Reason) : IncidentCommand;

public sealed record RequestExecutionCommand : IncidentCommand;

public sealed record ConfirmStartCommand(TenantId TenantId, EnvironmentId EnvironmentId, PlanId PlanId, int PlanVersion, long FencingToken) : IncidentCommand;

public sealed record RecordExecutionCommand(PlanId PlanId, int PlanVersion, ExecutionResult Result, string? Detail) : IncidentCommand;

public sealed record ScheduleRecoveryCommand : IncidentCommand;

public sealed record RecordRecoveryCommand(RecoveryResult Result, IReadOnlyList<Measurement> Measurements, DateTimeOffset ObservedThrough, string? Reason) : IncidentCommand;

public sealed record ResolveClearedCommand(string Reason) : IncidentCommand;

public sealed record LinkSignalCommand(ServiceId ServiceId, string MetricName, double Value, IReadOnlyList<ObservationId> ObservationIds, double Score, IReadOnlyList<string> Reasons) : IncidentCommand;

public sealed record ChangeSeverityCommand(IncidentSeverity Severity, string Reason) : IncidentCommand;

public sealed record ReopenCommand(string Reason, IReadOnlyList<ObservationId> Triggers, DateTimeOffset SymptomOnset) : IncidentCommand;

/// <summary>
/// What the workflow and message handlers may know about an incident: identifiers and small
/// summaries only (ADR-005), read from the authoritative aggregate, never from a projection.
/// </summary>
public sealed record IncidentSnapshot
{
    public required IncidentId IncidentId { get; init; }

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required ServiceId AffectedServiceId { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<ServiceId> AffectedServices { get; init; } = [];

    public required IncidentState State { get; init; }

    public required IncidentSeverity Severity { get; init; }

    public required long Version { get; init; }

    public required int Cycle { get; init; }

    public required DateTimeOffset SymptomOnset { get; init; }

    public IReadOnlyList<InvestigationId> PendingInvestigationIds { get; init; } = [];

    public int ReportCount { get; init; }

    public DecisionResult? Decision { get; init; }

    public double? DecisionConfidence { get; init; }

    public HypothesisId? LeadingHypothesisId { get; init; }

    public string? LeadingStatement { get; init; }

    public string? LeadingKey { get; init; }

    public ServiceId? LeadingServiceId { get; init; }

    /// <summary>The change the leading hypothesis blames, if any: what a rollback plan would revert.</summary>
    public LinkedChange? LeadingChange { get; init; }

    public IReadOnlyList<InvestigationId> ReportedInvestigationIds { get; init; } = [];

    public IReadOnlyList<string> LinkedDeploymentIds { get; init; } = [];

    /// <summary>Hypotheses of the current cycle, for the verifier.</summary>
    public IReadOnlyList<HypothesisId> HypothesisIds { get; init; } = [];

    public PlanId? PlanId { get; init; }

    public int? PlanVersion { get; init; }

    public PolicyVerdict? Policy { get; init; }

    public bool Approved { get; init; }

    public bool Rejected { get; init; }

    public long? FencingToken { get; init; }

    public bool ExecutionStarted { get; init; }

    public ExecutionResult? Execution { get; init; }

    /// <summary>The current plan's recovery criteria, which recovery verification measures.</summary>
    public IReadOnlyList<Sentinel.Contracts.Incidents.RecoveryCriterion> ExpectedRecovery { get; init; } = [];

    public DateTimeOffset? RecoveryWindowFrom { get; init; }

    public DateTimeOffset? RecoveryWindowTo { get; init; }

    public RecoveryResult? Recovery { get; init; }

    /// <summary>The observable features similar-incident retrieval compares (Phase 07).</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    public bool IsOpen => State != IncidentState.Resolved;

    public static IncidentSnapshot Of(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        Hypothesis? leader = incident.Decision?.LeadingHypothesisId is { } leading
            ? incident.AllHypotheses().FirstOrDefault(hypothesis => hypothesis.HypothesisId == leading)
            : null;
        return new IncidentSnapshot
        {
            IncidentId = incident.Id,
            Title = incident.Title,
            AffectedServices = incident.AffectedServices,
            LeadingStatement = leader?.Statement,
            LeadingKey = leader?.Key,
            LeadingServiceId = leader?.SuspectedServiceId,
            LeadingChange = leader?.SuspectedChangeObservationId is { } changeId
                ? incident.Changes.FirstOrDefault(change => change.ObservationIds.Contains(changeId))
                : null,
            ReportedInvestigationIds = [.. incident.Reports.Select(reported => reported.Report.InvestigationId)],
            LinkedDeploymentIds = [.. incident.Changes.Select(change => change.DeploymentId)],
            HypothesisIds = [.. incident.AllHypotheses().Select(hypothesis => hypothesis.HypothesisId).Distinct()],
            TenantId = incident.TenantId,
            EnvironmentId = incident.EnvironmentId,
            AffectedServiceId = incident.AffectedServiceId,
            State = incident.State,
            Severity = incident.Severity,
            Version = incident.Version,
            Cycle = incident.Cycle,
            SymptomOnset = incident.SymptomOnset,
            PendingInvestigationIds = [.. incident.PendingInvestigations.Select(pending => pending.InvestigationId)],
            ReportCount = incident.Reports.Count,
            Decision = incident.Decision?.Result,
            DecisionConfidence = incident.Decision?.Confidence,
            LeadingHypothesisId = incident.Decision?.LeadingHypothesisId,
            PlanId = incident.Plan?.PlanId,
            PlanVersion = incident.Plan?.Version,
            Policy = incident.Plan?.Policy,
            Approved = incident.Approval is not null,
            Rejected = incident.Rejection is not null,
            FencingToken = incident.Execution?.FencingToken,
            ExecutionStarted = incident.ExecutionStart is not null,
            Execution = incident.ExecutionOutcome?.Result,
            ExpectedRecovery = incident.Plan?.PlanDocument().ExpectedRecovery ?? [],
            RecoveryWindowFrom = incident.RecoveryWindow?.WindowFrom,
            RecoveryWindowTo = incident.RecoveryWindow?.WindowTo,
            Recovery = incident.Recovery?.Result,
            Features = Memory.MemoryFeatures.Observable(incident)
        };
    }
}

/// <summary>The writer's answer: whether new events were appended, and the state after the command.</summary>
public sealed record CommandReply(bool Changed, IncidentSnapshot Snapshot);
