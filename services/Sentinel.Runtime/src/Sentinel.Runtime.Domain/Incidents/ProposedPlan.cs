using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// One immutable plan version. The full plan document is kept as the canonical contract JSON it
/// was hashed from, so approval, execution and audit all refer to the same bytes.
/// </summary>
public sealed record ProposedPlan
{
    public required PlanId PlanId { get; init; }

    public required int Version { get; init; }

    public required string Hash { get; init; }

    public required HypothesisId HypothesisId { get; init; }

    public required ServiceId TargetServiceId { get; init; }

    public required TimeSpan ObservationWindow { get; init; }

    public required string Document { get; init; }

    public required PolicyVerdict Policy { get; init; }

    public required string PolicyVersion { get; init; }

    public IReadOnlyList<string> PolicyTrace { get; init; } = [];

    public required DateTimeOffset ProposedAt { get; init; }

    /// <summary>The plan's actions in operator terms, for an escalation that suggests rather than executes.</summary>
    public string? Summary { get; init; }

    /// <summary>When an unanswered approval request expires and the incident escalates (Phase 08); set only when approval is required, absent before Phase 08.</summary>
    public DateTimeOffset? ApprovalDeadline { get; init; }
}
