using System.Globalization;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Application.Live;

/// <summary>Live-event kinds, also the notification route names.</summary>
public static class LiveEventKinds
{
    public const string IncidentOpened = "incident-opened";
    public const string StatusChanged = "status-changed";
    public const string IncidentEscalated = "incident-escalated";
    public const string IncidentResolved = "incident-resolved";
    public const string EvidenceUpdated = "evidence-updated";
    public const string DecisionRecorded = "decision-recorded";
    public const string PlanProposed = "plan-proposed";
    public const string RecoveryEvaluated = "recovery-evaluated";

    public static readonly IReadOnlyList<string> All = [IncidentOpened, StatusChanged, IncidentEscalated, IncidentResolved, EvidenceUpdated, DecisionRecorded, PlanProposed, RecoveryEvaluated];
}

/// <summary>
/// A change to an incident, published to responders (Phase 08 live updates). It says what changed
/// and at which stream version; the query API stays the source of truth, so a client that receives
/// one refetches the incident rather than patching its copy.
/// </summary>
public sealed record LiveEvent(
    long Sequence,
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    IncidentId IncidentId,
    string Kind,
    long StreamVersion,
    string Summary,
    Severity? Severity,
    DateTimeOffset OccurredAt);

/// <summary>A live event before the store assigns its sequence.</summary>
public sealed record LiveEventDraft(
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    IncidentId IncidentId,
    string Kind,
    long StreamVersion,
    string Summary,
    Severity? Severity,
    DateTimeOffset OccurredAt);

/// <summary>Api's resumable live-event log: appended in the inbox transaction, read by sequence.</summary>
public interface ILiveEventStore
{
    Task AppendAsync(LiveEventDraft draft, CancellationToken cancellationToken);

    /// <summary>Events after <paramref name="after"/> in the given tenants, oldest first.</summary>
    Task<IReadOnlyList<LiveEvent>> AfterAsync(long after, IReadOnlyCollection<TenantId> tenants, int limit, CancellationToken cancellationToken);

    /// <summary>The newest sequence, where a client that asks for no history starts.</summary>
    Task<long> LatestAsync(CancellationToken cancellationToken);
}

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
