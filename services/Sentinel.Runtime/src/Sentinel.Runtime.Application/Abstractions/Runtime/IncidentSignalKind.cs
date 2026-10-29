namespace Sentinel.Runtime.Application.Abstractions.Runtime;

public enum IncidentSignalKind
{
    InvestigationReported,
    ApprovalRecorded,
    PlanRejected,
    ExecutionReported,
    Closed
}