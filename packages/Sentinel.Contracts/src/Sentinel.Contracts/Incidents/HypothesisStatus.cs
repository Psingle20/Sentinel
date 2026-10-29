using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

public enum HypothesisStatus
{
    Proposed,
    Supported,
    Challenged,
    Rejected,
    Confirmed,
    Superseded
}
