using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Domain.Approvals;

public enum ApprovalDecision
{
    Approved,
    Rejected,
    Revoked
}

/// <summary>
/// An approver's decision on one immutable plan version, as Api captured it. An approval carries
/// the attestation Api signed; Api owns this record, Runtime owns what it means for the incident.
/// </summary>
public sealed record ApprovalRecord
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
