using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

public sealed record RecoveryEvaluated(
    PlanId PlanId,
    RecoveryResult Result,
    IReadOnlyList<Measurement> Measurements,
    DateTimeOffset ObservedThrough,
    string? Reason,
    DateTimeOffset OccurredAt) : IIncidentEvent;
