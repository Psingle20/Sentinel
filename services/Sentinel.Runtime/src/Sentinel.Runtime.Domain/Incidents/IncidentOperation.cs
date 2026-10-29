namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>The incident operations whose permitted states <see cref="IncidentStateMachine"/> lists.</summary>
public enum IncidentOperation
{
    RequestInvestigation,
    RecordDecision,
    SupersedeDecision,
    EscalateWithoutPlan,
    Escalate,
    LinkSignal,
    ChangeSeverity,
    Reopen,
    Approve,
    Reject,
    RequestExecution,
    ConfirmStart,
    RecordRecovery,
    ResolveCleared
}
