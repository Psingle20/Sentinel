using System.Globalization;

namespace Sentinel.Executor.Domain.Policy;

public enum PolicyVerdict
{
    Allow,
    Deny,
    ApprovalRequired,
    SuggestionOnly
}
