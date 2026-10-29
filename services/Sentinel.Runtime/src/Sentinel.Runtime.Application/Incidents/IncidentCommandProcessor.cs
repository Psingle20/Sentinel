using System.Globalization;
using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Incidents.Commands;

namespace Sentinel.Runtime.Application.Incidents;

/// <summary>
/// Applies one <see cref="IncidentCommand"/> to an incident. Pure: no I/O and no clock beyond the
/// time it is given, so the same command on the same stream always appends the same events.
/// </summary>
public static class IncidentCommandProcessor
{
    /// <summary>The execution idempotency key Executor and the deploy controller deduplicate on (ADR-006).</summary>
    public static string IdempotencyKey(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        return string.Create(CultureInfo.InvariantCulture, $"{incident.TenantId}:{incident.Id}:execute:{incident.Plan!.PlanId}:{incident.Plan.Version}");
    }

    /// <summary>
    /// Returns the incident the command was applied to; <paramref name="incident"/> is null when the
    /// stream is empty, which only <see cref="OpenIncidentCommand"/> accepts.
    /// </summary>
    public static Result<Incident> Apply(IncidentId incidentId, Incident? incident, IncidentCommand command, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command is OpenIncidentCommand open)
        {
            return Result.Success(incident ?? Incident.Detect(incidentId, open.TenantId, open.EnvironmentId, open.ServiceId, open.Title, open.Severity, open.SymptomOnset, open.Triggers, open.Changes, now, open.TriggerMetric));
        }

        if (incident is null)
        {
            return Result.Failure<Incident>(IncidentErrors.NotFound);
        }

        Result result = command switch
        {
            RequestInvestigationsCommand request => RequestAll(incident, request, now),
            RecordInvestigationCommand record => Unit(incident.RecordInvestigation(record.Report, now)),
            TimeOutInvestigationsCommand timeOut => All(timeOut.InvestigationIds, id => incident.TimeOutInvestigation(id, now)),
            UpdateChangesCommand update => All(
                update.Changes.Where(change => update.LinkNew || incident.Changes.Any(existing => existing.DeploymentId == change.DeploymentId)),
                change => incident.UpdateChange(change, now)),
            RecordDecisionCommand { Supersede: { } reason } decide => incident.SupersedeDecision(decide.Decision, reason),
            RecordDecisionCommand decide => incident.RecordDecision(decide.Decision),
            DecideCommand decide => Decide(incident, decide, now),
            ProposePlanCommand propose => incident.ProposePlan(propose.Plan),
            EscalateCommand { WithoutPlan: true } escalate => incident.EscalateWithoutPlan(escalate.Reason, now),
            EscalateCommand escalate => incident.Escalate(escalate.Reason, now),
            ApprovePlanCommand approve => incident.Approve(approve.PlanId, approve.PlanVersion, approve.PlanHash, approve.ApproverId, approve.Attestation, now),
            RejectPlanCommand reject => incident.Reject(reject.PlanId, reject.PlanVersion, reject.RejectedBy, reject.Reason, now),
            RevokeApprovalCommand revoke => incident.RevokeApproval(revoke.PlanId, revoke.PlanVersion, revoke.RevokedBy, revoke.Reason, now),
            RequestExecutionCommand => Unit(incident.RequestExecution(incident.Plan is null ? string.Empty : IdempotencyKey(incident), now)),
            ConfirmStartCommand confirm => incident.TenantId == confirm.TenantId && incident.EnvironmentId == confirm.EnvironmentId
                ? incident.ConfirmStart(confirm.PlanId, confirm.PlanVersion, confirm.FencingToken, now)
                : Result.Failure(IncidentErrors.NotCurrentPlan),
            RecordExecutionCommand executed => incident.RecordExecution(executed.PlanId, executed.PlanVersion, executed.Result, executed.Detail, now),
            ScheduleRecoveryCommand => Unit(incident.ScheduleRecovery(now)),
            RecordRecoveryCommand recovery => incident.RecordRecovery(recovery.Result, recovery.Measurements, recovery.ObservedThrough, recovery.Reason, now),
            ResolveClearedCommand cleared => incident.ResolveCleared(cleared.Reason, now),
            LinkSignalCommand link => incident.LinkSignal(link.ServiceId, link.MetricName, link.Value, link.ObservationIds, link.Score, link.Reasons, now),
            ChangeSeverityCommand severity => incident.ChangeSeverity(severity.Severity, severity.Reason, now),
            ReopenCommand reopen => incident.Reopen(reopen.Reason, reopen.Triggers, reopen.SymptomOnset, now),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.GetType().Name, "Unknown incident command.")
        };

        return result.IsSuccess ? Result.Success(incident) : Result.Failure<Incident>(result.Error);
    }

    /// <summary>
    /// The decision the incident's current evidence implies. Pure, so a domain replay (Phase 07) can
    /// re-run a recorded decision on the stream prefix it was made from and compare.
    /// </summary>
    public static Decision Evaluate(Incident incident, bool useConsensus, ConsensusSettings settings, DecisionId decisionId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(settings);

        Decision decision = useConsensus
            ? Consensus.Decide(incident, [.. incident.Reports.SelectMany(reported => reported.Report.Challenges)], settings, decisionId, now) with { Settings = settings }
            : FalsificationCheck.Decide(incident, decisionId, now);
        return decision with { MissingEvidence = [.. incident.Reports.SelectMany(reported => reported.Report.Unknowns).Distinct(StringComparer.Ordinal)] };
    }

    /// <summary>Decides once per cycle; a decision already made stands, so a retried step is a no-op.</summary>
    private static Result Decide(Incident incident, DecideCommand decide, DateTimeOffset now) =>
        incident.Decision is not null
            ? Result.Success()
            : incident.RecordDecision(Evaluate(incident, decide.UseConsensus, decide.Settings, decide.DecisionId, now));

    private static Result RequestAll(Incident incident, RequestInvestigationsCommand request, DateTimeOffset now) =>
        All(request.Investigations, spec => incident.RequestInvestigation(spec.InvestigationId, spec.Objective, spec.WindowFrom, spec.WindowTo, now, spec.Role, spec.ChallengedHypothesisIds, spec.SimilarIncidents));

    private static Result All<T>(IEnumerable<T> items, Func<T, Result> apply)
    {
        foreach (T item in items)
        {
            Result result = apply(item);
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Result.Success();
    }

    private static Result Unit<T>(Result<T> result) => result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
}
