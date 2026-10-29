using System.Security.Cryptography;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Abstractions.Security;

namespace Sentinel.Runtime.Application.Services;

/// <summary>
/// Verifies an approver's decision before Runtime records it (ADR-006, review finding F-005): an ES256 attestation the
/// gateway signed with a published key, unexpired, for the decision's scope and audience, binding exactly this incident
/// and plan version and naming the approver the message names. A message on the shared bus proves nothing by itself.
/// </summary>
public sealed class DecisionVerifier(IApprovalKeyResolver keys, TimeProvider time)
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>Why the approval cannot be recorded, or null when it verifies. Approvals are addressed to Executor, which runs the plan.</summary>
    public Task<string?> ApprovalProblemAsync(string? attestation, IncidentId incidentId, PlanId planId, int planVersion, string planHash, string approverId, CancellationToken cancellationToken) =>
        ProblemAsync(attestation, ApprovalAttestation.ExecutePlanScope, ApprovalAttestation.Audience, incidentId, planId, planVersion, planHash, approverId, cancellationToken);

    /// <summary>Why the rejection or revocation cannot be recorded, or null when it verifies; both are addressed to Runtime.</summary>
    public Task<string?> DecisionProblemAsync(string? attestation, string scope, IncidentId incidentId, PlanId planId, int planVersion, string decidedBy, CancellationToken cancellationToken) =>
        ProblemAsync(attestation, scope, ApprovalAttestation.RuntimeAudience, incidentId, planId, planVersion, null, decidedBy, cancellationToken);

    private async Task<string?> ProblemAsync(
        string? attestation,
        string scope,
        string audience,
        IncidentId incidentId,
        PlanId planId,
        int planVersion,
        string? planHash,
        string decidedBy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(attestation))
        {
            return "The decision carries no attestation.";
        }

        if (!AttestationToken.TryDecode<ApprovalAttestationClaims>(attestation, out DecodedAttestation<ApprovalAttestationClaims> decoded, out string unreadable))
        {
            return unreadable;
        }

        if (decoded.Header is not { Algorithm: ApprovalAttestation.Algorithm, Type: ApprovalAttestation.TokenType })
        {
            return "The attestation must be an ES256 Sentinel approval attestation.";
        }

        ECDsa? key = await keys.ResolveAsync(decoded.Header.KeyId, cancellationToken);
        if (key is null)
        {
            return $"Key {decoded.Header.KeyId} is not a published approval key.";
        }

        if (!AttestationToken.VerifySignature(decoded, key))
        {
            return "The attestation signature is invalid.";
        }

        ApprovalAttestationClaims claims = decoded.Claims;
        DateTimeOffset now = time.GetUtcNow();
        return claims switch
        {
            { Issuer: not ApprovalAttestation.Issuer } => $"Issuer {claims.Issuer} is not the gateway.",
            _ when claims.Audience != audience => $"Audience {claims.Audience} is not {audience}.",
            _ when claims.ApprovalScope != scope => $"Scope {claims.ApprovalScope} is not {scope}.",
            _ when claims.ExpiresAt <= now - ClockSkew => $"The attestation expired at {claims.ExpiresAt:O}.",
            _ when claims.IssuedAt > now + ClockSkew => "The attestation was issued in the future.",
            _ when claims.IncidentId != incidentId => "The attestation is for another incident.",
            _ when claims.PlanId != planId || claims.PlanVersion != planVersion => "The attestation is for another plan version.",
            _ when planHash is not null && claims.PlanHash != planHash => "The attestation is for different plan content.",
            _ when claims.ApproverId != decidedBy => $"The attestation names {claims.ApproverId}, not {decidedBy}.",
            _ => null
        };
    }
}
