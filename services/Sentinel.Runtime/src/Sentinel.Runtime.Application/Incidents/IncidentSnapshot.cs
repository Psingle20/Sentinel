using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Domain.Incidents.Events;
using Sentinel.Runtime.Application.Projections;

namespace Sentinel.Runtime.Application.Incidents;

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

    /// <summary>The metric whose detection rule opened the incident.</summary>
    public string? TriggerMetric { get; init; }

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

    /// <summary>How many cycles followed a remedy that took effect without recovering the incident (Phase E03).</summary>
    public int FollowUps { get; init; }

    /// <summary>Remedies earlier cycles executed and verified at their target, with the verdict that followed: what a follow-up cycle knows was tried.</summary>
    public IReadOnlyList<TriedRemedy> TriedRemedies { get; init; } = [];

    /// <summary>The remedy whose unrecovered verdict opened the current cycle, when the cycle is a follow-up.</summary>
    public TriedRemedy? FollowingUp => TriedRemedies is [.., { FollowedUp: true } last] && last.Cycle == Cycle - 1 ? last : null;

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
            TriggerMetric = incident.TriggerMetric,
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
            FollowUps = incident.FollowUps,
            TriedRemedies = incident.TriedRemedies,
            Features = Memory.MemoryFeatures.Observable(incident)
        };
    }
}
