using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

public sealed record RecoveryScheduled(PlanId PlanId, DateTimeOffset WindowFrom, DateTimeOffset WindowTo, DateTimeOffset OccurredAt) : IIncidentEvent;
