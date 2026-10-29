using System.Globalization;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Application.Live;

/// <summary>Turns Runtime's lifecycle messages into live events (ADR-012 notification events).</summary>
public static class LiveEventMapper
{
    public static LiveEventDraft From(IncidentOpened opened)
    {
        ArgumentNullException.ThrowIfNull(opened);

        return new(opened.TenantId, opened.EnvironmentId, opened.IncidentId, LiveEventKinds.IncidentOpened, opened.StreamVersion, $"Incident opened: {opened.Title}", opened.Severity, opened.OpenedAt);
    }

    public static LiveEventDraft From(IncidentStatusChanged changed)
    {
        ArgumentNullException.ThrowIfNull(changed);

        string kind = changed.Status switch
        {
            IncidentStatus.Escalated => LiveEventKinds.IncidentEscalated,
            IncidentStatus.Resolved => LiveEventKinds.IncidentResolved,
            _ => LiveEventKinds.StatusChanged
        };
        string reason = changed.Reason is null ? string.Empty : $": {changed.Reason}";
        return new(changed.TenantId, changed.EnvironmentId, changed.IncidentId, kind, changed.StreamVersion, $"Status {changed.Status}{reason}", null, changed.ChangedAt);
    }

    public static LiveEventDraft From(IncidentBlackboardUpdated updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        return new(
            updated.TenantId, updated.EnvironmentId, updated.IncidentId, LiveEventKinds.EvidenceUpdated, updated.StreamVersion,
            string.Create(CultureInfo.InvariantCulture, $"The {updated.Role ?? "generalist"} investigator reported: {updated.AcceptedEvidence} evidence accepted, {updated.RejectedEvidence} rejected, {updated.HypothesisKeys.Count} hypotheses on the board."),
            null, updated.UpdatedAt);
    }

    public static LiveEventDraft From(DecisionRecorded decided)
    {
        ArgumentNullException.ThrowIfNull(decided);

        return new(
            decided.TenantId, decided.EnvironmentId, decided.IncidentId, LiveEventKinds.DecisionRecorded, decided.StreamVersion,
            string.Create(CultureInfo.InvariantCulture, $"{decided.Outcome} at confidence {decided.Confidence:F2}."),
            null, decided.DecidedAt);
    }

    public static LiveEventDraft From(RemediationPlanProposed proposed)
    {
        ArgumentNullException.ThrowIfNull(proposed);

        string action = (proposed.Plan.Actions.Count > 0 ? proposed.Plan.Actions[0] : null) switch
        {
            RollbackAction rollback => $"roll {rollback.TargetServiceId} back to {rollback.ToRevision}",
            PauseBatchAction pause => $"pause the {pause.TargetServiceId} batch",
            _ => "remediate"
        };
        return new(
            proposed.TenantId, proposed.EnvironmentId, proposed.IncidentId, LiveEventKinds.PlanProposed, proposed.StreamVersion,
            $"Plan v{proposed.Plan.PlanVersion} to {action}; policy says {proposed.PolicyEvaluation.Decision}.",
            null, proposed.PolicyEvaluation.EvaluatedAt);
    }

    public static LiveEventDraft From(RecoveryEvaluated evaluated)
    {
        ArgumentNullException.ThrowIfNull(evaluated);

        return new(evaluated.TenantId, evaluated.EnvironmentId, evaluated.IncidentId, LiveEventKinds.RecoveryEvaluated, evaluated.StreamVersion, $"Recovery {evaluated.Outcome}.", null, evaluated.EvaluatedAt);
    }
}
