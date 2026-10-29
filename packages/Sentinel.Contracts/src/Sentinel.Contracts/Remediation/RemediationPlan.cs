using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>
/// An immutable plan version. Approval binds to <see cref="PlanHash"/>, so any change needs a
/// new version and a new approval (ADR-006).
/// </summary>
public sealed record RemediationPlan
{
    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string PlanHash { get; init; }

    public required HypothesisId HypothesisId { get; init; }

    public required IReadOnlyList<RemediationAction> Actions { get; init; }

    public required IReadOnlyList<RecoveryCriterion> ExpectedRecovery { get; init; }

    /// <summary>How long to observe after the action before judging recovery.</summary>
    public required TimeSpan ObservationWindow { get; init; }

    public required RiskLevel Risk { get; init; }

    /// <summary>Conditions Executor re-checks at the target just before acting (Phase 06).</summary>
    public IReadOnlyList<PlanPrecondition>? Preconditions { get; init; }

    /// <summary>What the action should change at the target, in operator terms.</summary>
    public string? ExpectedEffect { get; init; }

    /// <summary>Every service the plan's actions can affect: its blast radius.</summary>
    public IReadOnlyList<ServiceId>? BlastRadius { get; init; }

    /// <summary>How a partly applied plan is undone, or why it cannot be undone automatically.</summary>
    public string? Compensation { get; init; }
}
