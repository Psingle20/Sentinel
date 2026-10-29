using Microsoft.Extensions.Options;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.Abstractions.Security;
using Sentinel.Gateway.Application.Options;
using Sentinel.Gateway.Domain.Approvals;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Approvals;

/// <summary>
/// Builds and signs the attestation for one approver decision (ADR-006). An approval is addressed to
/// Executor, which runs the plan; a rejection or revocation is addressed to Runtime, which records it.
/// Either way the claims bind the decision to the incident's current plan version and content, so a
/// forged or replayed decision cannot pass for the approver's.
/// </summary>
public sealed class DecisionAttestations(IApprovalSigner signer, IOptions<ApprovalOptions> options)
{
    public async Task<(ApprovalAttestationClaims Claims, SignedAttestation Signed)> SignAsync(
        string scope,
        IncidentState incident,
        PlanReference plan,
        string planHash,
        string approverId,
        string policyVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(plan);

        var claims = new ApprovalAttestationClaims
        {
            AttestationId = AttestationId.New(),
            TenantId = incident.TenantId,
            EnvironmentId = incident.EnvironmentId,
            IncidentId = incident.IncidentId,
            PlanId = plan.PlanId,
            PlanVersion = plan.PlanVersion,
            PlanHash = planHash,
            ApproverId = approverId,
            ApprovalScope = scope,
            PolicyVersion = policyVersion,
            IssuedAt = now,
            ExpiresAt = now + options.Value.AttestationLifetime,
            Issuer = ApprovalAttestation.Issuer,
            Audience = scope == ApprovalAttestation.ExecutePlanScope ? ApprovalAttestation.Audience : ApprovalAttestation.RuntimeAudience
        };
        SignedAttestation signed = await signer.SignAsync(claims, cancellationToken);
        return (claims, signed);
    }
}
