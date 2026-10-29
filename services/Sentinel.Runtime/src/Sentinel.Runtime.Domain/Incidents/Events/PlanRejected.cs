using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

public sealed record PlanRejected(PlanId PlanId, int PlanVersion, string RejectedBy, string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;
