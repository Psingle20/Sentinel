using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

public sealed record PlanApproved(PlanId PlanId, int PlanVersion, string PlanHash, string ApproverId, string Attestation, DateTimeOffset OccurredAt) : IIncidentEvent;
