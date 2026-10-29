using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Domain.Approvals;

/// <summary>
/// The stored state of an <see cref="Approval"/>: Infrastructure writes it and rebuilds the approval
/// from it with <see cref="Approval.Rehydrate"/>. The Gateway owns this record; Runtime owns what the
/// decision means for the incident.
/// </summary>
public sealed record ApprovalSnapshot
{
    public required Guid ApprovalId { get; init; }

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string PlanHash { get; init; }

    public required ApprovalDecision Decision { get; init; }

    public required string ActorId { get; init; }

    public string? Reason { get; init; }

    public AttestationId? AttestationId { get; init; }

    public string? KeyId { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Who withdrew the approval before execution started, and when (Phase 06).</summary>
    public string? RevokedBy { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }
}
