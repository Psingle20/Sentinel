using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// A remedy an earlier cycle of the incident executed and verified at its target, with the recovery verdict that
/// followed (Phase E03). <see cref="FollowedUp"/> marks the one whose unrecovered verdict opened a follow-up cycle.
/// </summary>
public sealed record RemedyTriedView(
    int Cycle,
    PlanId PlanId,
    ServiceId TargetServiceId,
    string Summary,
    RecoveryOutcome Outcome,
    IReadOnlyList<RecoveryMeasurement> Measurements,
    string? Reason,
    DateTimeOffset JudgedAt,
    bool FollowedUp);
