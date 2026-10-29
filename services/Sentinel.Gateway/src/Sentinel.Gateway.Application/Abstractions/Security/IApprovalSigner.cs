using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Application.Abstractions.Security;

/// <summary>
/// The Sentinel-owned signing interface (ADR-006). Locally a generated key pair; in production a
/// managed signing facility whose private key never leaves it.
/// </summary>
public interface IApprovalSigner
{
    Task<SignedAttestation> SignAsync(ApprovalAttestationClaims claims, CancellationToken cancellationToken);

    Task<ApprovalKeySet> PublishedKeysAsync(CancellationToken cancellationToken);
}