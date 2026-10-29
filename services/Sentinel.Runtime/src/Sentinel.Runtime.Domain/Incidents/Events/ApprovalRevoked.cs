using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

/// <summary>An approver withdrew an approval before execution started (Phase 06); no start can be confirmed for it.</summary>
public sealed record ApprovalRevoked(PlanId PlanId, int PlanVersion, string RevokedBy, string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;
