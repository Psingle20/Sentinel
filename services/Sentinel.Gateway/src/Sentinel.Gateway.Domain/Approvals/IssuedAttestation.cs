using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Domain.Approvals;

/// <summary>The attestation signed for an approval: its ID, the key that signed it and when it stops being valid (ADR-006).</summary>
public sealed record IssuedAttestation(AttestationId AttestationId, string KeyId, DateTimeOffset ExpiresAt);
