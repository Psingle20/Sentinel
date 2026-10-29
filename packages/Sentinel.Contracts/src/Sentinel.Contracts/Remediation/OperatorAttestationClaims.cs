using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>
/// What the Gateway attests when an authenticated operator changes policy or a control at an owning
/// service: who acted, which action, and a hash of exactly what they submitted, so the owning service
/// can refuse a replayed or altered request and record the operator from the claims rather than from
/// the body.
/// </summary>
public sealed record OperatorAttestationClaims
{
    public required AttestationId AttestationId { get; init; }

    public required string ActorId { get; init; }

    /// <summary>One of the operator scopes in <see cref="ApprovalAttestation"/>.</summary>
    public required string Scope { get; init; }

    /// <summary>The resource the action targets, for example <c>policy</c> or <c>controls/emergency-stop</c>.</summary>
    public required string Resource { get; init; }

    /// <summary>SHA-256 of the canonical content the operator submitted, prefixed <c>sha256:</c>.</summary>
    public required string ContentHash { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required string Issuer { get; init; }

    public required string Audience { get; init; }
}
