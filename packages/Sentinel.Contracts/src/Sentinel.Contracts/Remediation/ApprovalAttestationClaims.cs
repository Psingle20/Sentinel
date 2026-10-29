using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>The claims Api signs when an approver approves one immutable plan version (ADR-006).</summary>
public sealed record ApprovalAttestationClaims
{
    public required AttestationId AttestationId { get; init; }

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string PlanHash { get; init; }

    public required string ApproverId { get; init; }

    public required string ApprovalScope { get; init; }

    /// <summary>The policy version of the plan-time evaluation the approver saw.</summary>
    public required string PolicyVersion { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required string Issuer { get; init; }

    public required string Audience { get; init; }
}
