using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// The incident aggregate. Every change is an event appended to the incident's stream; state is
/// only ever derived by applying events, so replaying the stream rebuilds it exactly (ADR-004).
/// </summary>
public sealed class Incident : Entity
{
    private readonly List<LinkedChange> _changes = [];
    private readonly Dictionary<InvestigationId, InvestigationRequested> _investigations = [];
    private readonly List<InvestigationReported> _reports = [];
    private readonly HashSet<InvestigationId> _timedOut = [];
    private readonly List<Decision> _decisions = [];
    private readonly List<SignalLinked> _signals = [];
    private HypothesisBlackboard? _blackboard;

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

    public DateTimeOffset SymptomOnset { get; private set; }

    public IReadOnlyList<ObservationId> TriggerObservationIds { get; private set; } = [];

    /// <summary>The metric that opened the incident; null for streams recorded before Phase 07.</summary>
    public string? TriggerMetric { get; private set; }

    public IncidentState State { get; private set; }

    /// <summary>The version of the last event applied; the next event gets <c>Version + 1</c>.</summary>
    public long Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsOpen => State != IncidentState.Resolved;

    public IReadOnlyList<LinkedChange> Changes => _changes;

    public IReadOnlyCollection<InvestigationRequested> Investigations => _investigations.Values;

    /// <summary>Reports of the current cycle; earlier cycles of a reopened incident stay in the stream.</summary>
    public IReadOnlyList<InvestigationReported> Reports => _reports;

    /// <summary>Requested investigations that have neither reported nor timed out.</summary>
    public IReadOnlyList<InvestigationRequested> PendingInvestigations =>
        [.. _investigations.Values.Where(requested => !_timedOut.Contains(requested.InvestigationId) && !_reports.Exists(report => report.Report.InvestigationId == requested.InvestigationId))];

    public IReadOnlyCollection<InvestigationId> TimedOutInvestigationIds => _timedOut;

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

    public ProposedPlan? Plan { get; private set; }

    public PlanApproved? Approval { get; private set; }

    public PlanRejected? Rejection { get; private set; }

    public ExecutionRequested? Execution { get; private set; }

    public ExecutionStarted? ExecutionStart { get; private set; }

    public ExecutionReported? ExecutionOutcome { get; private set; }

    public RecoveryScheduled? RecoveryWindow { get; private set; }

    public RecoveryEvaluated? Recovery { get; private set; }

    public string? ClosingReason { get; private set; }

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
        if (_investigations.ContainsKey(investigationId))
        {
            return Result.Success();
        }

        if (State is not (IncidentState.Detected or IncidentState.Investigating) || Decision is not null)
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "request an investigation"));
        }

        if (challengedHypothesisIds is { Count: > 0 } challenged && challenged.Any(id => !AllHypotheses().Any(hypothesis => hypothesis.HypothesisId == id)))
        {
            return Result.Failure(IncidentErrors.UnknownHypothesis);
        }

        Append(new InvestigationRequested(investigationId, objective, windowFrom, windowTo, now, role, challengedHypothesisIds, similarIncidents is { Count: > 0 } ? similarIncidents : null));
        return Result.Success();
    }

    /// <summary>Gives up waiting for an investigation; a report that still arrives later is recorded as history.</summary>
    public Result TimeOutInvestigation(InvestigationId investigationId, DateTimeOffset now)
    {
        if (!PendingInvestigations.Any(pending => pending.InvestigationId == investigationId))
        {
            return Result.Success();
        }

        Append(new InvestigationTimedOut(investigationId, now));
        return Result.Success();
    }

    /// <summary>
    /// Records findings. Runtime, not the agent, decides what becomes evidence: findings that cite
    /// no observation, have an inverted time range or point at unknown hypotheses are rejected.
    /// A repeated report for the same investigation is ignored.
    /// </summary>
    public Result<bool> RecordInvestigation(InvestigationReport report, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!_investigations.ContainsKey(report.InvestigationId))
        {
            return Result.Failure<bool>(IncidentErrors.UnknownInvestigation(report.InvestigationId));
        }

        if (_reports.Exists(existing => existing.Report.InvestigationId == report.InvestigationId))
        {
            return Result.Success(false);
        }

        // Evidence may link to the report's own hypotheses or to any already on the blackboard,
        // which is how the verifier's findings attach to the hypotheses it challenges.
        HashSet<HypothesisId> hypotheses = [.. report.Hypotheses.Select(hypothesis => hypothesis.HypothesisId), .. Blackboard.All.SelectMany(entry => entry.Lineage.Append(entry.Hypothesis.HypothesisId))];
        var rejected = new List<RejectedEvidence>();
        foreach (Evidence evidence in report.Evidence)
        {
            string? reason = evidence switch
            {
                { SourceObservationIds.Count: 0 } => "cites no observation",
                _ when evidence.To < evidence.From => "has an inverted time range",
                _ when evidence.Strength is < 0 or > 1 => "has a strength outside 0 to 1",
                _ when evidence.HypothesisIds.Any(id => !hypotheses.Contains(id)) => "refers to an unknown hypothesis",
                _ => null
            };

            if (reason is not null)
            {
                rejected.Add(new RejectedEvidence(evidence.EvidenceId, reason));
            }
        }

        Append(new InvestigationReported(report, rejected, now));
        return Result.Success(true);
    }

    /// <summary>The current cycle's evidence blackboard: merged hypotheses with their lineage (Phase 04).</summary>
    public HypothesisBlackboard Blackboard => _blackboard ??= HypothesisBlackboard.Build(_reports);

    /// <summary>Accepted evidence from every report, in report order, linked to the blackboard's hypotheses.</summary>
    public IReadOnlyList<Evidence> AcceptedEvidence() =>
        [.. _reports.SelectMany(report => report.Report.Evidence
            .Where(evidence => !report.RejectedEvidence.Any(rejected => rejected.EvidenceId == evidence.EvidenceId)))
            .Select(Blackboard.Remap)];

    /// <summary>The competing explanations: proposals merged by key, broader ones replaced by their refinements.</summary>
    public IReadOnlyList<Hypothesis> AllHypotheses() => Blackboard.Competing;

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

        if (State != IncidentState.Investigating)
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

        if (Execution is not null || State is not (IncidentState.Investigating or IncidentState.AwaitingApproval or IncidentState.Escalated))
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "supersede the decision"));
        }

        Append(new DecisionSuperseded(Decision.DecisionId, next.DecisionId, reason, next.DecidedAt));
        return RecordDecision(next);
    }

    public Result ProposePlan(ProposedPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (Plan is { } current && current.PlanId == plan.PlanId && current.Version == plan.Version)
        {
            return current.Hash == plan.Hash ? Result.Success() : Result.Failure(IncidentErrors.StalePlanVersion);
        }

        if (Decision is not { Result: DecisionResult.Diagnosed } decision || decision.LeadingHypothesisId != plan.HypothesisId)
        {
            return Result.Failure(IncidentErrors.PlanWithoutDiagnosis);
        }

        if (Plan is not null && plan.Version <= Plan.Version)
        {
            return Result.Failure(IncidentErrors.StalePlanVersion);
        }

        Append(new PlanProposed(plan, plan.ProposedAt));
        if (plan.Policy == PolicyVerdict.Deny)
        {
            Append(new IncidentEscalated($"Policy {plan.PolicyVersion} denies the plan; human remediation is required.", plan.ProposedAt));
        }
        else if (plan.Policy == PolicyVerdict.SuggestionOnly)
        {
            Append(new IncidentEscalated($"Suggested remediation, which policy {plan.PolicyVersion} leaves to responders: {plan.Summary ?? "see the plan"}.", plan.ProposedAt));
        }

        return Result.Success();
    }

    /// <summary>Diagnosed, but there is no automated remediation for the leading cause.</summary>
    public Result EscalateWithoutPlan(string reason, DateTimeOffset now)
    {
        if (State == IncidentState.Escalated && Plan is null)
        {
            return Result.Success();
        }

        if (Decision is not { Result: DecisionResult.Diagnosed } || Plan is not null || State != IncidentState.Investigating)
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
        if (State is IncidentState.Resolved or IncidentState.Escalated)
        {
            return Result.Success();
        }

        Append(new IncidentEscalated(reason, now));
        return Result.Success();
    }

    /// <summary>Merges a related service's symptom into this incident (correlation, Phase 03).</summary>
    public Result LinkSignal(ServiceId serviceId, string metricName, double value, IReadOnlyList<ObservationId> observationIds, double score, IReadOnlyList<string> reasons, DateTimeOffset now)
    {
        if (!IsOpen)
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
        if (severity == Severity || !IsOpen)
        {
            return Result.Success();
        }

        Append(new SeverityChanged(severity, reason, now));
        return Result.Success();
    }

    /// <summary>The symptom returned soon after resolution with no new change to explain it: start a new cycle.</summary>
    public Result Reopen(string reason, IReadOnlyList<ObservationId> triggers, DateTimeOffset symptomOnset, DateTimeOffset now)
    {
        if (State != IncidentState.Resolved)
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "reopen"));
        }

        Append(new IncidentReopened(reason, triggers, symptomOnset, now));
        return Result.Success();
    }

    /// <summary>Relays Api's signed approval. Only the current plan version, with its exact hash, can be approved.</summary>
    public Result Approve(PlanId planId, int planVersion, string planHash, string approverId, string attestation, DateTimeOffset now)
    {
        Result current = RequireCurrentPlan(planId, planVersion, planHash);
        if (current.IsFailure)
        {
            return current;
        }

        if (Approval is not null)
        {
            return Approval.PlanVersion == planVersion ? Result.Success() : Result.Failure(IncidentErrors.AlreadyApproved);
        }

        if (State != IncidentState.AwaitingApproval)
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "approve"));
        }

        Append(new PlanApproved(planId, planVersion, planHash, approverId, attestation, now));
        return Result.Success();
    }

    public Result Reject(PlanId planId, int planVersion, string rejectedBy, string reason, DateTimeOffset now)
    {
        if (Plan is null || Plan.PlanId != planId || Plan.Version != planVersion)
        {
            return Result.Failure(IncidentErrors.NotCurrentPlan);
        }

        if (Rejection is { } existing && existing.PlanId == planId && existing.PlanVersion == planVersion)
        {
            return Result.Success();
        }

        if (State != IncidentState.AwaitingApproval)
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "reject"));
        }

        Append(new PlanRejected(planId, planVersion, rejectedBy, reason, now));
        Append(new IncidentEscalated($"{rejectedBy} rejected the plan: {reason}", now));
        return Result.Success();
    }

    /// <summary>
    /// Withdraws an approval before execution starts (Phase 06). The incident is handed to responders
    /// and Runtime refuses any start confirmation, so a requested execution can never begin.
    /// </summary>
    public Result RevokeApproval(PlanId planId, int planVersion, string revokedBy, string reason, DateTimeOffset now)
    {
        if (Plan is null || Plan.PlanId != planId || Plan.Version != planVersion)
        {
            return Result.Failure(IncidentErrors.NotCurrentPlan);
        }

        if (Approval is null)
        {
            return Result.Success();
        }

        if (ExecutionStart is not null)
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "revoke an approval after execution started"));
        }

        Append(new ApprovalRevoked(planId, planVersion, revokedBy, reason, now));
        Append(new IncidentEscalated($"{revokedBy} revoked the approval of plan v{planVersion}: {reason}", now));
        return Result.Success();
    }

    /// <summary>Requests execution of the approved plan. The new event's stream version is the fencing token.</summary>
    public Result<ExecutionRequested> RequestExecution(string idempotencyKey, DateTimeOffset now)
    {
        if (Execution is not null)
        {
            return Result.Success(Execution);
        }

        if (Approval is null || Plan is null || State != IncidentState.AwaitingApproval)
        {
            return Result.Failure<ExecutionRequested>(IncidentErrors.InvalidState(State, "request execution"));
        }

        var requested = new ExecutionRequested(Plan.PlanId, Plan.Version, Version + 1, idempotencyKey, now);
        Append(requested);
        return Result.Success(requested);
    }

    /// <summary>
    /// Executor's start confirmation (ADR-006). Confirmed only for the current approved plan and the
    /// fencing token it was issued with, while the incident is still remediating. A repeated
    /// confirmation for the same token is confirmed again without a new event.
    /// </summary>
    public Result ConfirmStart(PlanId planId, int planVersion, long fencingToken, DateTimeOffset now)
    {
        if (Execution is null || Execution.PlanId != planId || Execution.PlanVersion != planVersion)
        {
            return Result.Failure(IncidentErrors.NotCurrentPlan);
        }

        if (Execution.FencingToken != fencingToken)
        {
            return Result.Failure(IncidentErrors.StaleFencingToken(fencingToken, Execution.FencingToken));
        }

        if (ExecutionStart is not null)
        {
            return Result.Success();
        }

        if (State != IncidentState.Remediating || ExecutionOutcome is not null)
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "start remediation"));
        }

        Append(new ExecutionStarted(planId, planVersion, fencingToken, now));
        return Result.Success();
    }

    /// <summary>Records Executor's result. <see cref="ExecutionResult.OutcomeUnknown"/> keeps the incident remediating until reconciled.</summary>
    public Result RecordExecution(PlanId planId, int planVersion, ExecutionResult result, string? detail, DateTimeOffset now)
    {
        if (Execution is null || Execution.PlanId != planId || Execution.PlanVersion != planVersion)
        {
            return Result.Failure(IncidentErrors.NotCurrentPlan);
        }

        if (ExecutionOutcome is { Result: not ExecutionResult.OutcomeUnknown })
        {
            return Result.Success();
        }

        Append(new ExecutionReported(planId, planVersion, result, detail, now));
        if (result is ExecutionResult.Rejected or ExecutionResult.Aborted or ExecutionResult.Failed or ExecutionResult.Compensated or ExecutionResult.CompensationFailed)
        {
            Append(new IncidentEscalated($"Remediation ended {result}: {detail}", now));
        }

        return Result.Success();
    }

    /// <summary>After the action is verified, recovery is judged over the plan's observation window starting now.</summary>
    public Result<RecoveryScheduled> ScheduleRecovery(DateTimeOffset now)
    {
        if (RecoveryWindow is not null)
        {
            return Result.Success(RecoveryWindow);
        }

        if (ExecutionOutcome is not { Result: ExecutionResult.ActionVerified } || Plan is null)
        {
            return Result.Failure<RecoveryScheduled>(IncidentErrors.InvalidState(State, "schedule recovery verification"));
        }

        var scheduled = new RecoveryScheduled(Plan.PlanId, now, now + Plan.ObservationWindow, now);
        Append(scheduled);
        return Result.Success(scheduled);
    }

    public Result RecordRecovery(RecoveryResult result, IReadOnlyList<Measurement> measurements, DateTimeOffset observedThrough, string? reason, DateTimeOffset now)
    {
        if (Recovery is not null && RecoveryWindow is not null && Recovery.PlanId == RecoveryWindow.PlanId)
        {
            return Result.Success();
        }

        if (RecoveryWindow is null || State != IncidentState.Verifying)
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "record recovery"));
        }

        Append(new RecoveryEvaluated(RecoveryWindow.PlanId, result, measurements, observedThrough, reason, now));
        if (result == RecoveryResult.Recovered)
        {
            Append(new IncidentResolved("Recovery verified from telemetry.", now));
        }
        else
        {
            Append(new IncidentEscalated($"Recovery {result}: {reason}", now));
        }

        return Result.Success();
    }

    /// <summary>
    /// Symptoms cleared without Sentinel's action, for example after a human stopped a batch. A
    /// pending plan is moot: approving a rollback after recovery would be harmful.
    /// </summary>
    public Result ResolveCleared(string reason, DateTimeOffset now)
    {
        if (State == IncidentState.Resolved)
        {
            return Result.Success();
        }

        if (State is not (IncidentState.AwaitingApproval or IncidentState.Escalated))
        {
            return Result.Failure(IncidentErrors.InvalidState(State, "resolve as cleared"));
        }

        Append(new IncidentResolved(reason, now));
        return Result.Success();
    }

    private Result RequireCurrentPlan(PlanId planId, int planVersion, string planHash)
    {
        if (Plan is null || Plan.PlanId != planId || Plan.Version != planVersion)
        {
            return Result.Failure(IncidentErrors.NotCurrentPlan);
        }

        return Plan.Hash == planHash ? Result.Success() : Result.Failure(IncidentErrors.PlanHashMismatch);
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
                _changes.AddRange(detected.LinkedChanges);
                State = IncidentState.Detected;
                break;
            case InvestigationRequested requested:
                _investigations[requested.InvestigationId] = requested;
                State = IncidentState.Investigating;
                break;
            case InvestigationReported reported:
                _reports.Add(reported);
                _blackboard = null;
                break;
            case InvestigationTimedOut timedOut:
                _timedOut.Add(timedOut.InvestigationId);
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
                Plan = null;
                Approval = null;
                Rejection = null;
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
                SymptomOnset = reopened.SymptomOnset;
                TriggerObservationIds = reopened.TriggerObservationIds;
                _reports.Clear();
                _blackboard = null;
                Decision = null;
                Plan = null;
                Approval = null;
                Rejection = null;
                Execution = null;
                ExecutionStart = null;
                ExecutionOutcome = null;
                RecoveryWindow = null;
                Recovery = null;
                ClosingReason = null;
                ResolvedAt = null;
                State = IncidentState.Detected;
                break;
            case PlanProposed proposed:
                Plan = proposed.Plan;
                Approval = null;
                State = proposed.Plan.Policy is PolicyVerdict.Deny or PolicyVerdict.SuggestionOnly ? State : IncidentState.AwaitingApproval;
                break;
            case PlanApproved approved:
                Approval = approved;
                break;
            case PlanRejected rejected:
                Rejection = rejected;
                break;
            case ApprovalRevoked:
                Approval = null;
                break;
            case ExecutionRequested execution:
                Execution = execution;
                State = IncidentState.Remediating;
                break;
            case ExecutionStarted started:
                ExecutionStart = started;
                break;
            case ExecutionReported reported:
                ExecutionOutcome = reported;
                break;
            case RecoveryScheduled scheduled:
                RecoveryWindow = scheduled;
                State = IncidentState.Verifying;
                break;
            case RecoveryEvaluated evaluated:
                Recovery = evaluated;
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
            default:
                throw new ArgumentOutOfRangeException(nameof(@event), @event.GetType().Name, "Unknown incident event.");
        }

        Version++;
        UpdatedAt = @event.OccurredAt;
    }
}

public static class IncidentErrors
{
    public static readonly Error AlreadyDecided = Error.Conflict("Incident.AlreadyDecided", "The incident already has a decision.");

    public static readonly Error PlanWithoutDiagnosis = Error.Conflict("Incident.PlanWithoutDiagnosis", "A plan must remediate the diagnosed leading hypothesis.");

    public static readonly Error StalePlanVersion = Error.Conflict("Incident.StalePlanVersion", "A new plan must have a higher version than the current one.");

    public static readonly Error NotCurrentPlan = Error.Conflict("Incident.NotCurrentPlan", "The plan is not the incident's current plan.");

    public static readonly Error PlanHashMismatch = Error.Conflict("Incident.PlanHashMismatch", "The approved hash does not match the current plan's content.");

    public static readonly Error AlreadyApproved = Error.Conflict("Incident.AlreadyApproved", "Another plan version is already approved.");

    public static readonly Error UnknownHypothesis = Error.Problem("Incident.UnknownHypothesis", "A challenged hypothesis is not among the incident's hypotheses.");

    public static Error InvalidState(IncidentState state, string action) =>
        Error.Conflict("Incident.InvalidState", $"Cannot {action} while the incident is {state}.");

    public static Error UnknownInvestigation(InvestigationId investigationId) =>
        Error.NotFound("Incident.UnknownInvestigation", $"Investigation {investigationId} was not requested for this incident.");

    public static Error StaleFencingToken(long presented, long current) =>
        Error.Conflict("Incident.StaleFencingToken", $"Fencing token {presented} does not match the current execution's {current}.");
}
