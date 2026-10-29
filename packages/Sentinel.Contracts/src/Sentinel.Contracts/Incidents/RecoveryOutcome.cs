using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

public enum RecoveryOutcome
{
    Recovered,
    Unchanged,
    Degraded,
    Inconclusive,
    CompensationRequired
}
