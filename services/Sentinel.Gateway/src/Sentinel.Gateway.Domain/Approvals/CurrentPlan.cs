using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Domain.Approvals;

/// <summary>
/// The incident's current plan version as Runtime reports it: its content hash, whether that hash
/// still matches the content, and its status.
/// </summary>
public sealed record CurrentPlan(PlanId PlanId, int PlanVersion, string PlanHash, bool ContentMatchesHash, string Status, bool AwaitingApproval);
