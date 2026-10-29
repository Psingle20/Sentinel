using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>A plan with its policy verdict and approval state; <see cref="ApprovalDeadline"/> is when an unanswered approval request expires (Phase 08).</summary>
public sealed record PlanView(RemediationPlan Plan, PolicyEvaluation PolicyEvaluation, PlanStatus Status, string? ApprovedBy, DateTimeOffset? ApprovedAt, DateTimeOffset? ApprovalDeadline = null);
