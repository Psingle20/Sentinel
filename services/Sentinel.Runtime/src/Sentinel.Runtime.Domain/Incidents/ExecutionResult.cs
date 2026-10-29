using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public enum ExecutionResult
{
    Rejected,
    Aborted,
    ActionVerified,
    Failed,
    OutcomeUnknown,
    Compensated,
    CompensationFailed
}
