using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public enum IncidentState
{
    Detected,
    Investigating,
    AwaitingApproval,
    Remediating,
    Verifying,
    Resolved,
    Escalated
}
