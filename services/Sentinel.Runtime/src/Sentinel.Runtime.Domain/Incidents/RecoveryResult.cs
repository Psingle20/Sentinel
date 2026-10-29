using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public enum RecoveryResult
{
    Recovered,
    Unchanged,
    Degraded,
    Inconclusive,
    CompensationRequired
}
