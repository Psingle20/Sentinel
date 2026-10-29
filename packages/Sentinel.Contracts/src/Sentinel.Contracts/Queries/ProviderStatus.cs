using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Queries;

public enum ProviderStatus
{
    Healthy,
    Degraded,
    Unavailable
}
