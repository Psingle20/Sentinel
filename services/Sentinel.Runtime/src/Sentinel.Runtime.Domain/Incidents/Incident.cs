using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// The incident aggregate. Every change is an event appended to the incident's stream; state is
/// only ever derived by applying events, so replaying the stream rebuilds it exactly (ADR-004). It
/// stays one root with one stream; its lifecycles are child entities, <see cref="Investigation"/>,
/// <see cref="Remediation"/> and <see cref="RecoveryVerification"/>, each with its own transition
/// table, and its own states follow <see cref="IncidentStateMachine"/>.
/// </summary>
public sealed class Incident : Entity
{
    private readonly List<LinkedChange> _changes = [];
    private readonly List<Decision> _decisions = [];
    private readonly List<SignalLinked> _signals = [];
    private readonly Investigation _investigation = new();
    private readonly Remediation _remediation = new();
    private readonly RecoveryVerification _recovery = new();

    private Incident()
    {
    }

    public IncidentId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public EnvironmentId EnvironmentId { get; private set; }

    public ServiceId AffectedServiceId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public IncidentSeverity Severity { get; private set; }

    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>When the current cycle began: the detection, or the latest reopening. Cycle timings run from here.</summary>
    public DateTimeOffset CycleStartedAt { get; private set; }

    public DateTimeOffset SymptomOnset { get; private set; }

    public IReadOnlyList<ObservationId> TriggerObservationIds { get; private set; } = [];

    /// <summary>The metric that opened the incident; null for streams recorded before Phase 07.</summary>
    public string? TriggerMetric { get; private set; }

    public IncidentState State { get; private set; }

    /// <summary>The version of the last event applied; the next event gets <c>Version + 1</c>.</summary>
    public long Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsOpen => IncidentStateMachine.IsOpen(State);

    public IReadOnlyList<LinkedChange> Changes => _changes;

    public IReadOnlyCollection<InvestigationRequested> Investigations => _investigation.Requested;

    /// <summary>Reports of the current cycle; earlier cycles of a reopened incident stay in the stream.</summary>
    public IReadOnlyList<InvestigationReported> Reports => _investigation.Reports;

    /// <summary>Requested investigations that have neither reported nor timed out.</summary>
    public IReadOnlyList<InvestigationRequested> PendingInvestigations => _investigation.Pending;

    public IReadOnlyCollection<InvestigationId> TimedOutInvestigationIds => _investigation.TimedOut;

    /// <summary>Every decision made, oldest first, superseded ones included (Phase 05).</summary>
    public IReadOnlyList<Decision> Decisions => _decisions;

    /// <summary>Related services' symptoms correlation merged into this incident.</summary>
    public IReadOnlyList<SignalLinked> LinkedSignals => _signals;

    /// <summary>The affected service and every service whose symptom was merged in: the blast radius.</summary>
    public IReadOnlyList<ServiceId> AffectedServices => [AffectedServiceId, .. _signals.Select(signal => signal.ServiceId).Where(service => service != AffectedServiceId).Distinct()];

    /// <summary>How many times the incident has been reopened; each cycle has its own workflow run.</summary>
    public int Cycle { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public Decision? Decision { get; private set; }

    public ProposedPlan? Plan => _remediation.Plan;

    public PlanApproved? Approval => _remediation.Approval;

    public PlanRejected? Rejection => _remediation.Rejection;

    public ExecutionRequested? Execution => _remediation.Execution;

    public ExecutionStarted? ExecutionStart => _remediation.ExecutionStart;

    public ExecutionReported? ExecutionOutcome => _remediation.ExecutionOutcome;

    public RecoveryScheduled? RecoveryWindow => _recovery.Window;

    public RecoveryEvaluated? Recovery => _recovery.Evaluation;

    public string? ClosingReason { get; private set; }

    /// <summary>The current cycle's evidence blackboard: merged hypotheses with their lineage (Phase 04).</summary>
    public HypothesisBlackboard Blackboard => _investigation.Blackboard;

    public static Incident Detect(
        IncidentId incidentId,
        TenantId tenantId,
        EnvironmentId environmentId,
        ServiceId serviceId,
        string title,
        IncidentSeverity severity,
        DateTimeOffset symptomOnset,
        IReadOnlyList<ObservationId> triggers,
        IReadOnlyList<LinkedChange> linkedChanges,
        DateTimeOffset now,
        string? triggerMetric = null)
    {
        var incident = new Incident();
        incident.Append(new IncidentDetected(incidentId, tenantId, environmentId, serviceId, title, severity, symptomOnset, triggers, linkedChanges, now, triggerMetric));
        return incident;
    }

    /// <summary>Rebuilds an incident from its stream, oldest event first.</summary>
    public static Incident Rehydrate(IEnumerable<IIncidentEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var incident = new Incident();
        foreach (IIncidentEvent @event in history)
        {
            incident.Apply(@event);
        }

        return incident.Version == 0
            ? throw new InvalidOperationException("An incident stream starts with IncidentDetected.")
            : incident;
    }

    /// <summary>
    /// Assigns an investigation. Repeating a request with the same ID changes nothing, so a retried
    /// workflow step cannot start a second investigation (ADR-004).
    /// </summary>
    public Result RequestInvestigation(
        InvestigationId investigationId,
        string objective,
        DateTimeOffset windowFrom,
        DateTimeOffset windowTo,
        DateTimeOffset now,
        string? role = null,
        IReadOnlyList<HypothesisId>? challengedHypothesisIds = null,
        IReadOnlyList<PriorIncident>? similarIncidents = null)
    {
        if (_investigation.Has(investigationId))
        {
            return Result.Success();
        }

        if (!IncidentStateMachine.Allows(State, IncidentOperation.RequestInvestigation) || Decision is not null)
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "request an investigation"));
        }

        Result<InvestigationRequested> requested = _investigation.Request(investigationId, objective, windowFrom, windowTo, now, role, challengedHypothesisIds, similarIncidents);
        if (requested.IsFailure)
        {
            return Result.Failure(requested.Error);
        }

        Append(requested.Value);
        return Result.Success();
    }

    /// <summary>Gives up waiting for an investigation; a report that still arrives later is recorded as history.</summary>
    public Result TimeOutInvestigation(InvestigationId investigationId, DateTimeOffset now)
    {
        if (_investigation.IsPending(investigationId))
        {
            Append(new InvestigationTimedOut(investigationId, now));
        }

        return Result.Success();
    }

    /// <summary>
    /// Records findings. Runtime, not the agent, decides what becomes evidence (see
    /// <see cref="Investigation"/>). A repeated report for the same investigation is ignored.
    /// </summary>
    public Result<bool> RecordInvestigation(InvestigationReport report, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(report);

        Result<InvestigationReported?> reported = _investigation.Record(report, now);
        if (reported.IsFailure)
        {
            return Result.Failure<bool>(reported.Error);
        }

        if (reported.Value is not { } recorded)
        {
            return Result.Success(false);
        }

        Append(recorded);
        return Result.Success(true);
    }

    /// <summary>Accepted evidence from every report, in report order, linked to the blackboard's hypotheses.</summary>
    public IReadOnlyList<Evidence> AcceptedEvidence() => _investigation.AcceptedEvidence();

    /// <summary>The competing explanations: proposals merged by key, broader ones replaced by their refinements.</summary>
    public IReadOnlyList<Hypothesis> AllHypotheses() => _investigation.AllHypotheses();

    /// <summary>Applies what is now known about a correlated change, for example its completion time.</summary>
    public Result UpdateChange(LinkedChange change, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(change);

        LinkedChange? existing = _changes.Find(current => current.DeploymentId == change.DeploymentId);
        if (existing is not null && existing.CompletedAt == change.CompletedAt && existing.Failed == change.Failed)
        {
            return Result.Success();
        }

        Append(new ChangeUpdated(change, now));
        return Result.Success();
    }

    public Result RecordDecision(Decision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (Decision is not null)
        {
            return Decision.DecisionId == decision.DecisionId ? Result.Success() : Result.Failure(IncidentErrors.AlreadyDecided);
        }

        if (!IncidentStateMachine.Allows(State, IncidentOperation.RecordDecision))
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "decide"));
        }

        Append(new DecisionMade(decision, decision.DecidedAt));
        if (decision.Result == DecisionResult.Abstained)
        {
            Append(new IncidentEscalated($"Sentinel abstained: {decision.Rationale}", decision.DecidedAt));
        }

        return Result.Success();
    }

    /// <summary>
    /// A later observation supersedes the current decision before anything executed (Phase 05). The
    /// superseded decision, its plan and any approval stay in the history; the new decision applies.
    /// </summary>
    public Result SupersedeDecision(Decision next, string reason)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (Decision is null || Decision.DecisionId == next.DecisionId)
        {
            return Decision is null ? RecordDecision(next) : Result.Success();
        }

        if (Execution is not null || !IncidentStateMachine.Allows(State, IncidentOperation.SupersedeDecision))
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "supersede the decision"));
        }

        Append(new DecisionSuperseded(Decision.DecisionId, next.DecisionId, reason, next.DecidedAt));
        return RecordDecision(next);
    }

    public Result ProposePlan(ProposedPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return AppendAll(_remediation.Propose(plan, Decision, State));
    }

    /// <summary>Diagnosed, but there is no automated remediation for the leading cause.</summary>
    public Result EscalateWithoutPlan(string reason, DateTimeOffset now)
    {
        // Already escalated without a plan: nothing to do.
        if (Plan is null && State == IncidentState.Escalated)
        {
            return Result.Success();
        }

        if (Decision is not { Result: DecisionResult.Diagnosed } || Plan is not null || !IncidentStateMachine.Allows(State, IncidentOperation.EscalateWithoutPlan))
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "escalate without a plan"));
        }

        Append(new IncidentEscalated(reason, now));
        return Result.Success();
    }

    /// <summary>
    /// Hands the incident to humans, for example when an approval wait expires or the workflow is
    /// cancelled. An escalated incident refuses any later start confirmation (ADR-006).
    /// </summary>
    public Result Escalate(string reason, DateTimeOffset now)
    {
        if (IncidentStateMachine.Allows(State, IncidentOperation.Escalate))
        {
            Append(new IncidentEscalated(reason, now));
        }

        return Result.Success();
    }

    /// <summary>Merges a related service's symptom into this incident (correlation, Phase 03).</summary>
    public Result LinkSignal(ServiceId serviceId, string metricName, double value, IReadOnlyList<ObservationId> observationIds, double score, IReadOnlyList<string> reasons, DateTimeOffset now)
    {
        if (!IncidentStateMachine.Allows(State, IncidentOperation.LinkSignal))
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "link a signal"));
        }

        if (serviceId == AffectedServiceId || _signals.Exists(signal => signal.ServiceId == serviceId && signal.MetricName == metricName))
        {
            return Result.Success();
        }

        Append(new SignalLinked(serviceId, metricName, value, observationIds, score, reasons, now));
        return Result.Success();
    }

    public Result ChangeSeverity(IncidentSeverity severity, string reason, DateTimeOffset now)
    {
        if (severity != Severity && IncidentStateMachine.Allows(State, IncidentOperation.ChangeSeverity))
        {
            Append(new SeverityChanged(severity, reason, now));
        }

        return Result.Success();
    }

    /// <summary>The symptom returned soon after resolution with no new change to explain it: start a new cycle.</summary>
    public Result Reopen(string reason, IReadOnlyList<ObservationId> triggers, DateTimeOffset symptomOnset, DateTimeOffset now)
    {
        if (!IncidentStateMachine.Allows(State, IncidentOperation.Reopen))
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "reopen"));
        }

        Append(new IncidentReopened(reason, triggers, symptomOnset, now));
        return Result.Success();
    }

    /// <summary>Relays the Gateway's signed approval. Only the current plan version, with its exact hash, can be approved.</summary>
    public Result Approve(PlanId planId, int planVersion, string planHash, string approverId, string attestation, DateTimeOffset now) =>
        AppendAll(_remediation.Approve(planId, planVersion, planHash, approverId, attestation, now, State));

    public Result Reject(PlanId planId, int planVersion, string rejectedBy, string reason, DateTimeOffset now) =>
        AppendAll(_remediation.Reject(planId, planVersion, rejectedBy, reason, now, State));

    /// <summary>
    /// Withdraws an approval before execution starts (Phase 06). The incident is handed to responders
    /// and Runtime refuses any start confirmation, so a requested execution can never begin.
    /// </summary>
    public Result RevokeApproval(PlanId planId, int planVersion, string revokedBy, string reason, DateTimeOffset now) =>
        AppendAll(_remediation.Revoke(planId, planVersion, revokedBy, reason, now, State));

    /// <summary>Requests execution of the approved plan. The new event's stream version is the fencing token.</summary>
    public Result<ExecutionRequested> RequestExecution(string idempotencyKey, DateTimeOffset now)
    {
        ExecutionRequested? existing = Execution;
        Result<ExecutionRequested> requested = _remediation.RequestExecution(idempotencyKey, Version + 1, now, State);
        if (requested.IsSuccess && !ReferenceEquals(requested.Value, existing))
        {
            Append(requested.Value);
        }

        return requested;
    }

    /// <summary>
    /// Executor's start confirmation (ADR-006). Confirmed only for the current approved plan and the
    /// fencing token it was issued with, while the incident is still remediating. A repeated
    /// confirmation for the same token is confirmed again without a new event.
    /// </summary>
    public Result ConfirmStart(PlanId planId, int planVersion, long fencingToken, DateTimeOffset now) =>
        AppendAll(_remediation.ConfirmStart(planId, planVersion, fencingToken, now, State));

    /// <summary>Records Executor's result. <see cref="ExecutionResult.OutcomeUnknown"/> keeps the incident remediating until reconciled.</summary>
    public Result RecordExecution(PlanId planId, int planVersion, ExecutionResult result, string? detail, DateTimeOffset now) =>
        AppendAll(_remediation.RecordExecution(planId, planVersion, result, detail, now));

    /// <summary>After the action is verified, recovery is judged over the plan's observation window starting now.</summary>
    public Result<RecoveryScheduled> ScheduleRecovery(DateTimeOffset now)
    {
        RecoveryScheduled? existing = RecoveryWindow;
        Result<RecoveryScheduled> scheduled = _recovery.Schedule(_remediation, now, State);
        if (scheduled.IsSuccess && !ReferenceEquals(scheduled.Value, existing))
        {
            Append(scheduled.Value);
        }

        return scheduled;
    }

    public Result RecordRecovery(RecoveryResult result, IReadOnlyList<Measurement> measurements, DateTimeOffset observedThrough, string? reason, DateTimeOffset now) =>
        AppendAll(_recovery.Record(result, measurements, observedThrough, reason, now, State));

    /// <summary>
    /// Symptoms cleared without Sentinel's action, for example after a human stopped a batch. A
    /// pending plan is moot: approving a rollback after recovery would be harmful.
    /// </summary>
    public Result ResolveCleared(string reason, DateTimeOffset now)
    {
        // Already resolved: nothing to do.
        if (State == IncidentState.Resolved)
        {
            return Result.Success();
        }

        if (!IncidentStateMachine.Allows(State, IncidentOperation.ResolveCleared))
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "resolve as cleared"));
        }

        Append(new IncidentResolved(reason, now));
        return Result.Success();
    }

    private Result AppendAll(Result<IReadOnlyList<IIncidentEvent>> decided)
    {
        if (decided.IsFailure)
        {
            return Result.Failure(decided.Error);
        }

        foreach (IIncidentEvent @event in decided.Value)
        {
            Append(@event);
        }

        return Result.Success();
    }

    private void Append(IIncidentEvent @event)
    {
        Apply(@event);
        Raise(@event);
    }

    private void Apply(IIncidentEvent @event)
    {
        switch (@event)
        {
            case IncidentDetected detected:
                Id = detected.IncidentId;
                TenantId = detected.TenantId;
                EnvironmentId = detected.EnvironmentId;
                AffectedServiceId = detected.ServiceId;
                Title = detected.Title;
                Severity = detected.Severity;
                SymptomOnset = detected.SymptomOnset;
                TriggerObservationIds = detected.TriggerObservationIds;
                TriggerMetric = detected.TriggerMetric;
                DetectedAt = detected.OccurredAt;
                CycleStartedAt = detected.OccurredAt;
                _changes.AddRange(detected.LinkedChanges);
                State = IncidentState.Detected;
                break;
            case InvestigationRequested:
                State = IncidentState.Investigating;
                break;
            case ChangeUpdated updated:
                _changes.RemoveAll(change => change.DeploymentId == updated.Change.DeploymentId);
                _changes.Add(updated.Change);
                break;
            case DecisionMade made:
                Decision = made.Decision;
                _decisions.Add(made.Decision);
                break;
            case DecisionSuperseded superseded:
                int index = _decisions.FindIndex(decision => decision.DecisionId == superseded.DecisionId);
                if (index >= 0)
                {
                    _decisions[index] = _decisions[index] with { SupersededBy = superseded.SupersededBy };
                }

                Decision = null;
                ClosingReason = null;
                State = IncidentState.Investigating;
                break;
            case SignalLinked linked:
                _signals.Add(linked);
                break;
            case SeverityChanged changed:
                Severity = changed.Severity;
                break;
            case IncidentReopened reopened:
                Cycle++;
                CycleStartedAt = reopened.OccurredAt;
                SymptomOnset = reopened.SymptomOnset;
                TriggerObservationIds = reopened.TriggerObservationIds;
                Decision = null;
                ClosingReason = null;
                ResolvedAt = null;
                State = IncidentState.Detected;
                break;
            case PlanProposed proposed:
                State = proposed.Plan.Policy is PolicyVerdict.Deny or PolicyVerdict.SuggestionOnly ? State : IncidentState.AwaitingApproval;
                break;
            case ExecutionRequested:
                State = IncidentState.Remediating;
                break;
            case RecoveryScheduled:
                State = IncidentState.Verifying;
                break;
            case IncidentResolved resolved:
                State = IncidentState.Resolved;
                ClosingReason = resolved.Reason;
                ResolvedAt = resolved.OccurredAt;
                break;
            case IncidentEscalated escalated:
                State = IncidentState.Escalated;
                ClosingReason = escalated.Reason;
                break;
            case InvestigationReported or InvestigationTimedOut or PlanApproved or PlanRejected or ApprovalRevoked or ExecutionStarted or ExecutionReported or RecoveryEvaluated:
                // Held by the child entities alone.
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(@event), @event.GetType().Name, "Unknown incident event.");
        }

        _investigation.Apply(@event);
        _remediation.Apply(@event);
        _recovery.Apply(@event);
        Version++;
        UpdatedAt = @event.OccurredAt;
    }
}
