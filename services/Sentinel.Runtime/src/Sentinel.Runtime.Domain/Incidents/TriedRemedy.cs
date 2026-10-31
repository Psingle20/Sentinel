using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// A remedy an earlier cycle of the incident executed and verified at its target, with the recovery verdict that
/// followed. The incident keeps it across cycles, so a follow-up cycle knows what was tried and never proposes it again.
/// </summary>
public sealed record TriedRemedy(
    int Cycle,
    PlanId PlanId,
    ServiceId TargetServiceId,
    string Summary,
    RecoveryResult Result,
    IReadOnlyList<Measurement> Measurements,
    string? Reason,
    DateTimeOffset JudgedAt,
    bool FollowedUp);
