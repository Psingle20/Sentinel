using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Domain.Approvals;

/// <summary>The plan version an approver says they reviewed.</summary>
public sealed record PlanReference(PlanId PlanId, int PlanVersion);
