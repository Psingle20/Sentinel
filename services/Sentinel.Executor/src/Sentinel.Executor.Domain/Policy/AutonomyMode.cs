using System.Globalization;

namespace Sentinel.Executor.Domain.Policy;

/// <summary>An environment's autonomy mode: the most any rule may grant there (Phase 06).</summary>
public enum AutonomyMode
{
    ReadOnly,
    SuggestionOnly,
    ApprovalRequired,
    ConstrainedAutonomous
}
