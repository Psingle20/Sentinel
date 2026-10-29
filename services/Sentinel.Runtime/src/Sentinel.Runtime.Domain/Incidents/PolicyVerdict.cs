using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public enum PolicyVerdict
{
    Allow,
    Deny,
    ApprovalRequired,
    SuggestionOnly
}
