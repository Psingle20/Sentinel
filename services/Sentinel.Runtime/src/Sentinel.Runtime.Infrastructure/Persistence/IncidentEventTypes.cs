using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using RecoveryEvaluated = Sentinel.Runtime.Domain.Incidents.Events.RecoveryEvaluated;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Infrastructure.Persistence;

/// <summary>
/// Persisted event type names. They are part of the stored stream, so a name never changes once
/// written; a new event shape gets a new name or an upcaster (Phase 07).
/// </summary>
internal static class IncidentEventTypes
{
    private static readonly Dictionary<string, Type> ByName = new(StringComparer.Ordinal)
    {
        ["incident.detected.v1"] = typeof(IncidentDetected),
        ["investigation.requested.v1"] = typeof(InvestigationRequested),
        ["investigation.reported.v1"] = typeof(InvestigationReported),
        ["change.updated.v1"] = typeof(ChangeUpdated),
        ["decision.made.v1"] = typeof(DecisionMade),
        ["plan.proposed.v1"] = typeof(PlanProposed),
        ["plan.approved.v1"] = typeof(PlanApproved),
        ["plan.rejected.v1"] = typeof(PlanRejected),
        ["approval.revoked.v1"] = typeof(ApprovalRevoked),
        ["execution.requested.v1"] = typeof(ExecutionRequested),
        ["execution.started.v1"] = typeof(ExecutionStarted),
        ["execution.reported.v1"] = typeof(ExecutionReported),
        ["recovery.scheduled.v1"] = typeof(RecoveryScheduled),
        ["recovery.evaluated.v1"] = typeof(RecoveryEvaluated),
        ["incident.resolved.v1"] = typeof(IncidentResolved),
        ["incident.escalated.v1"] = typeof(IncidentEscalated),
        ["investigation.timed-out.v1"] = typeof(InvestigationTimedOut),
        ["decision.superseded.v1"] = typeof(DecisionSuperseded),
        ["signal.linked.v1"] = typeof(SignalLinked),
        ["severity.changed.v1"] = typeof(SeverityChanged),
        ["incident.reopened.v1"] = typeof(IncidentReopened)
    };

    private static readonly Dictionary<Type, string> ByType = ByName.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static string NameOf(IIncidentEvent @event) => ByType[@event.GetType()];

    public static IIncidentEvent Deserialize(string name, string payload) =>
        (IIncidentEvent)JsonSerializer.Deserialize(IncidentEventUpcasters.Upcast(name, payload), ByName[name], SentinelJson.Options)!;

    public static string Serialize(IIncidentEvent @event) => JsonSerializer.Serialize(@event, @event.GetType(), SentinelJson.Options);
}
