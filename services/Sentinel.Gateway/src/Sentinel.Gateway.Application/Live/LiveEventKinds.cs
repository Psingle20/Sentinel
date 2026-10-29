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
