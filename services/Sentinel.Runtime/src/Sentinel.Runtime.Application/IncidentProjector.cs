using System.Globalization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;
using DomainDecision = Sentinel.Runtime.Domain.Incidents.Decision;
using DomainRecoveryEvaluated = Sentinel.Runtime.Domain.Incidents.RecoveryEvaluated;

namespace Sentinel.Runtime.Application;

/// <summary>
/// Builds the incident read model from the stream. It is a pure function of the events, so the
/// read model can be deleted and rebuilt at any time (Phase 03 exit criterion).
/// </summary>
public static class IncidentProjector
{
    public static IncidentDetail Project(IReadOnlyList<StoredEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var incident = Incident.Rehydrate(history.Select(stored => stored.Event));
        DomainDecision? decision = incident.Decision;

        return new IncidentDetail
        {
            Summary = Summary(incident),
            TriggerObservationIds = incident.TriggerObservationIds,
            LinkedDeploymentObservationIds = [.. incident.Changes.SelectMany(change => change.ObservationIds)],
            Timeline = [.. history.Select(stored => new TimelineEntry(stored.Version, stored.Event.OccurredAt, Kind(stored.Event), Describe(stored.Event)))],
            Investigations = [.. incident.Investigations.Select(requested => Investigation(requested, incident))],
            Evidence = [.. incident.Reports.SelectMany(reported => reported.Report.Evidence.Select(evidence =>
            {
                RejectedEvidence? rejection = reported.RejectedEvidence.FirstOrDefault(rejected => rejected.EvidenceId == evidence.EvidenceId);
                Evidence shown = rejection is null ? incident.Blackboard.Remap(evidence) : evidence;
                return new EvidenceView(shown.ToContract(), reported.Report.InvestigationId, rejection is null ? EvidenceStatus.Accepted : EvidenceStatus.Rejected, rejection?.Reason);
            }))],
            Hypotheses = [.. incident.Blackboard.All.Select(entry => new HypothesisView(
                entry.Hypothesis.ToContract(),
                entry.ProposedBy[0],
                entry.Subsumed ? HypothesisStatus.Superseded : HypothesisState(entry.Hypothesis, decision, incident),
                entry.Lineage,
                entry.ProposedBy,
                entry.Refines?.ToString()))],
            Decision = decision?.ToView(),
            Plan = incident.Plan is { } plan
                ? new PlanView(plan.PlanDocument(), plan.PolicyEvaluation(), PlanState(incident), incident.Approval?.ApproverId, incident.Approval?.OccurredAt, plan.ApprovalDeadline)
                : null,
            Execution = incident.Execution is { } execution
                ? new ExecutionView(
                    execution.PlanId,
                    execution.PlanVersion,
                    incident.ExecutionOutcome?.Result.ToContract(),
                    incident.ExecutionOutcome?.Detail,
                    execution.OccurredAt,
                    incident.ExecutionStart?.OccurredAt,
                    incident.ExecutionOutcome?.OccurredAt)
                : null,
            Recovery = incident.Recovery is { } recovery
                ? new RecoveryView(recovery.PlanId, recovery.Result.ToContract(), [.. recovery.Measurements.Select(measurement => measurement.ToContract())], recovery.ObservedThrough, recovery.OccurredAt, recovery.Reason)
                : null,
            AffectedServices = incident.AffectedServices,
            Changes = [.. incident.Changes.OrderByDescending(change => change.CorrelationScore ?? 0).Select(change => change.ToView())],
            LinkedSignals = [.. incident.LinkedSignals.Select(signal => new LinkedSignalView(signal.ServiceId, signal.MetricName, signal.Value, signal.Score, signal.Reasons, signal.OccurredAt))],
            Challenges = [.. incident.Reports.SelectMany(reported => reported.Report.Challenges.Select(challenge => challenge.ToView(reported.Report.InvestigationId)))],
            Decisions = [.. incident.Decisions.Select(made => made.ToView())],
            Cycle = incident.Cycle,
            CausalGraph = CausalGraph(incident)
        };
    }

    /// <summary>
    /// The causal graph (Phase 04): the symptom and the services it affects, the correlated changes,
    /// the blackboard's hypotheses and the evidence for and against them. Derived, never stored apart.
    /// </summary>
    public static CausalGraphView CausalGraph(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var nodes = new Dictionary<string, CausalNode>(StringComparer.Ordinal);
        var edges = new List<CausalEdge>();
        void Node(string id, CausalNodeKind kind, string label) => nodes.TryAdd(id, new CausalNode(id, kind, label));
        string Service(ServiceId service)
        {
            string id = $"service:{service}";
            Node(id, CausalNodeKind.Service, service.Value);
            return id;
        }

        const string Symptom = "symptom";
        Node(Symptom, CausalNodeKind.Symptom, incident.Title);
        foreach (ServiceId affected in incident.AffectedServices)
        {
            edges.Add(new CausalEdge(Symptom, Service(affected), "affects"));
        }

        foreach (LinkedChange change in incident.Changes)
        {
            string id = $"change:{change.DeploymentId}";
            Node(id, CausalNodeKind.Change, $"{change.ServiceId} {change.ChangeKind} {change.Version}");
            edges.Add(new CausalEdge(id, Service(change.ServiceId), "changed", change.CorrelationScore));
        }

        foreach (BlackboardHypothesis entry in incident.Blackboard.All)
        {
            Hypothesis hypothesis = entry.Hypothesis;
            string id = $"hypothesis:{hypothesis.HypothesisId}";
            Node(id, CausalNodeKind.Hypothesis, hypothesis.Statement);
            edges.Add(new CausalEdge(id, Symptom, "explains"));
            if (entry.Refines is { } broader)
            {
                edges.Add(new CausalEdge(id, $"hypothesis:{broader}", "refines"));
            }

            if (hypothesis.SuspectedServiceId is { } suspected)
            {
                edges.Add(new CausalEdge(id, Service(suspected), "implicates"));
            }

            if (hypothesis.SuspectedChangeObservationId is { } changeObservation
                && incident.Changes.FirstOrDefault(change => change.ObservationIds.Contains(changeObservation)) is { } blamed)
            {
                edges.Add(new CausalEdge(id, $"change:{blamed.DeploymentId}", "blames"));
            }
        }

        foreach (Evidence evidence in incident.AcceptedEvidence())
        {
            string id = $"evidence:{evidence.EvidenceId}";
            Node(id, CausalNodeKind.Evidence, evidence.Statement);
            foreach (HypothesisId hypothesis in evidence.HypothesisIds)
            {
                edges.Add(new CausalEdge(id, $"hypothesis:{hypothesis}", evidence.Direction == Sentinel.Runtime.Domain.Incidents.EvidenceDirection.Contradicts ? "contradicts" : "supports", evidence.Strength));
            }
        }

        return new CausalGraphView([.. nodes.Values], [.. edges.Where(edge => nodes.ContainsKey(edge.From) && nodes.ContainsKey(edge.To)).Distinct()]);
    }

    public static IncidentSummary Summary(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        return new IncidentSummary
        {
            TenantId = incident.TenantId,
            EnvironmentId = incident.EnvironmentId,
            IncidentId = incident.Id,
            AffectedServiceId = incident.AffectedServiceId,
            Title = incident.Title,
            Severity = incident.Severity.ToContract(),
            Status = incident.State.ToStatus(),
            OpenedAt = incident.DetectedAt,
            UpdatedAt = incident.UpdatedAt,
            StreamVersion = incident.Version,
            SymptomOnset = incident.SymptomOnset
        };
    }

    private static InvestigationView Investigation(InvestigationRequested requested, Incident incident)
    {
        InvestigationReport? report = incident.Reports.FirstOrDefault(reported => reported.Report.InvestigationId == requested.InvestigationId)?.Report;
        return new InvestigationView(
            requested.InvestigationId,
            requested.OccurredAt,
            report?.AgentExecutionId,
            report?.Result switch
            {
                null => null,
                InvestigationResult.Completed => InvestigationOutcome.Completed,
                InvestigationResult.Abstained => InvestigationOutcome.Abstained,
                _ => InvestigationOutcome.Failed
            },
            report is null ? null : new ModelMetadata(report.ModelProvider, report.Model, report.ModelVersion, report.PromptVersion),
            report?.AbstentionReason,
            report?.CompletedAt,
            requested.Role,
            report is null && incident.TimedOutInvestigationIds.Contains(requested.InvestigationId));
    }

    private static HypothesisStatus HypothesisState(Hypothesis hypothesis, DomainDecision? decision, Incident incident)
    {
        if (decision is null)
        {
            return HypothesisStatus.Proposed;
        }

        if (decision.LeadingHypothesisId == hypothesis.HypothesisId)
        {
            return incident.Recovery?.Result == RecoveryResult.Recovered ? HypothesisStatus.Confirmed : HypothesisStatus.Supported;
        }

        if (incident.Decisions.Any(earlier => earlier.SupersededBy is not null && earlier.LeadingHypothesisId == hypothesis.HypothesisId))
        {
            return HypothesisStatus.Superseded;
        }

        return decision.RejectedHypothesisIds.Contains(hypothesis.HypothesisId) ? HypothesisStatus.Rejected : HypothesisStatus.Challenged;
    }

    private static PlanStatus PlanState(Incident incident)
    {
        if (incident.Plan!.Policy == PolicyVerdict.Deny)
        {
            return PlanStatus.Denied;
        }

        if (incident.Plan.Policy == PolicyVerdict.SuggestionOnly)
        {
            return PlanStatus.SuggestionOnly;
        }

        return incident switch
        {
            { ExecutionOutcome.Result: ExecutionResult.ActionVerified } => PlanStatus.Executed,
            { ExecutionOutcome.Result: not ExecutionResult.OutcomeUnknown } => PlanStatus.Failed,
            { Execution: not null } => PlanStatus.Executing,
            { Rejection: not null } => PlanStatus.Rejected,
            { Approval: not null } => PlanStatus.Approved,
            { State: IncidentState.Resolved } => PlanStatus.Superseded,
            _ => PlanStatus.AwaitingApproval
        };
    }

    private static string ChangeState(LinkedChange change)
    {
        if (change.Failed)
        {
            return "failed";
        }

        return change.CompletedAt is null ? "is rolling out" : "is serving";
    }

    private static string Kind(IIncidentEvent @event) => @event switch
    {
        IncidentDetected => "incident.detected",
        InvestigationRequested => "investigation.requested",
        InvestigationReported => "investigation.reported",
        ChangeUpdated => "change.updated",
        DecisionMade => "decision.made",
        PlanProposed => "plan.proposed",
        PlanApproved => "plan.approved",
        PlanRejected => "plan.rejected",
        ApprovalRevoked => "approval.revoked",
        ExecutionRequested => "execution.requested",
        ExecutionStarted => "execution.started",
        ExecutionReported => "execution.reported",
        RecoveryScheduled => "recovery.scheduled",
        DomainRecoveryEvaluated => "recovery.evaluated",
        IncidentResolved => "incident.resolved",
        IncidentEscalated => "incident.escalated",
        InvestigationTimedOut => "investigation.timed-out",
        DecisionSuperseded => "decision.superseded",
        SignalLinked => "signal.linked",
        SeverityChanged => "severity.changed",
        IncidentReopened => "incident.reopened",
        _ => "unknown"
    };

    private static string Describe(IIncidentEvent @event) => @event switch
    {
        IncidentDetected detected => string.Create(CultureInfo.InvariantCulture, $"{detected.Title}; {detected.LinkedChanges.Count} recent change(s) correlated."),
        InvestigationRequested { Role: { } role } requested => $"{role} investigation requested: {requested.Objective}",
        InvestigationRequested requested => $"Investigation requested: {requested.Objective}",
        InvestigationTimedOut => "An investigation did not report within its budget; the incident proceeds without it.",
        DecisionSuperseded superseded => $"Decision superseded: {superseded.Reason}",
        SignalLinked linked => string.Create(CultureInfo.InvariantCulture, $"{linked.ServiceId} {linked.MetricName} at {linked.Value:F1} merged into this incident (score {linked.Score:F2}): {string.Join("; ", linked.Reasons)}"),
        SeverityChanged changed => $"Severity {changed.Severity}: {changed.Reason}",
        IncidentReopened reopened => $"Reopened: {reopened.Reason}",
        InvestigationReported reported => string.Create(CultureInfo.InvariantCulture, $"{reported.Report.Model} investigation {reported.Report.Result}: {reported.Report.Hypotheses.Count} hypotheses, {reported.Report.Evidence.Count - reported.RejectedEvidence.Count} accepted evidence, {reported.RejectedEvidence.Count} rejected."),
        ChangeUpdated updated => $"{updated.Change.ServiceId} {updated.Change.Version} {ChangeState(updated.Change)}.",
        DecisionMade made => $"{made.Decision.Result}: {made.Decision.Rationale}",
        PlanProposed proposed => $"Plan v{proposed.Plan.Version} proposed for {proposed.Plan.TargetServiceId}; policy {proposed.Plan.PolicyVersion}: {proposed.Plan.Policy}.",
        PlanApproved approved => $"Plan v{approved.PlanVersion} approved by {approved.ApproverId}.",
        PlanRejected rejected => $"Plan v{rejected.PlanVersion} rejected by {rejected.RejectedBy}: {rejected.Reason}",
        ApprovalRevoked revoked => $"Approval of plan v{revoked.PlanVersion} revoked by {revoked.RevokedBy}: {revoked.Reason}",
        ExecutionRequested requested => string.Create(CultureInfo.InvariantCulture, $"Execution of plan v{requested.PlanVersion} requested with fencing token {requested.FencingToken}."),
        ExecutionStarted => "Executor confirmed the start; the action is under way.",
        ExecutionReported reported => $"Executor reported {reported.Result}: {reported.Detail}",
        RecoveryScheduled scheduled => $"Recovery is observed from {scheduled.WindowFrom:O} to {scheduled.WindowTo:O}.",
        DomainRecoveryEvaluated evaluated => $"Recovery {evaluated.Result}{(evaluated.Reason is null ? "." : $": {evaluated.Reason}")}",
        IncidentResolved resolved => $"Resolved: {resolved.Reason}",
        IncidentEscalated escalated => $"Escalated to responders: {escalated.Reason}",
        _ => @event.GetType().Name
    };
}
