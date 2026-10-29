using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

public enum IncidentStatus
{
    Detected,
    Investigating,
    AwaitingApproval,
    Remediating,
    Verifying,
    Resolved,
    Escalated
}
