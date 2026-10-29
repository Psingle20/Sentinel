using Microsoft.Extensions.Options;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.Abstractions.Security;
using Sentinel.Gateway.Application.Options;

namespace Sentinel.Gateway.Application.ControlPlane;

/// <summary>
/// Signs what an authenticated operator submits to an owning service (ADR-006): the actor, the scope,
/// the resource and a hash of the content, valid for one request. The owning service verifies it with
/// the Gateway's published keys and records the actor from these claims, so no caller can change policy
/// or a control by naming an operator in a body.
/// </summary>
public sealed class OperatorAttestations(IApprovalSigner signer, IOptions<ApprovalOptions> options, TimeProvider time)
{
    public async Task<string> IssueAsync(string actorId, string scope, string resource, string contentHash, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        var claims = new OperatorAttestationClaims
        {
            AttestationId = AttestationId.New(),
            ActorId = actorId,
            Scope = scope,
            Resource = resource,
            ContentHash = contentHash,
            IssuedAt = now,
            ExpiresAt = now + options.Value.OperatorAttestationLifetime,
            Issuer = ApprovalAttestation.Issuer,
            Audience = ApprovalAttestation.Audience
        };
        SignedAttestation signed = await signer.SignOperatorAsync(claims, cancellationToken);
        return signed.Token;
    }
}
