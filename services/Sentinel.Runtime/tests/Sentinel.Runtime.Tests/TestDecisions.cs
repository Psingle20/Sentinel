using System.Security.Cryptography;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Abstractions.Security;

namespace Sentinel.Runtime.Tests;

/// <summary>Approver decisions signed as the gateway signs them, with a test key that <see cref="Keys"/> publishes.</summary>
internal static class TestDecisions
{
    public const string KeyId = "test-approval-key";

    private static readonly ECDsa SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static string Approve(IncidentId incidentId, PlanId planId, int planVersion, string planHash, string approverId, DateTimeOffset now) =>
        Sign(ApprovalAttestation.ExecutePlanScope, ApprovalAttestation.Audience, incidentId, planId, planVersion, planHash, approverId, now);

    public static string Reject(IncidentId incidentId, PlanId planId, int planVersion, string approverId, DateTimeOffset now) =>
        Sign(ApprovalAttestation.RejectPlanScope, ApprovalAttestation.RuntimeAudience, incidentId, planId, planVersion, "sha256:plan", approverId, now);

    public static string Revoke(IncidentId incidentId, PlanId planId, int planVersion, string approverId, DateTimeOffset now) =>
        Sign(ApprovalAttestation.RevokeApprovalScope, ApprovalAttestation.RuntimeAudience, incidentId, planId, planVersion, "sha256:plan", approverId, now);

    public static string Sign(string scope, string audience, IncidentId incidentId, PlanId planId, int planVersion, string planHash, string approverId, DateTimeOffset now, ECDsa? key = null, TimeSpan? lifetime = null) =>
        AttestationToken.Encode(
            new ApprovalAttestationHeader(ApprovalAttestation.Algorithm, KeyId, ApprovalAttestation.TokenType),
            new ApprovalAttestationClaims
            {
                AttestationId = AttestationId.New(),
                TenantId = InMemoryRuntime.Tenant,
                EnvironmentId = InMemoryRuntime.Environment,
                IncidentId = incidentId,
                PlanId = planId,
                PlanVersion = planVersion,
                PlanHash = planHash,
                ApproverId = approverId,
                ApprovalScope = scope,
                PolicyVersion = "policy-test",
                IssuedAt = now,
                ExpiresAt = now + (lifetime ?? TimeSpan.FromDays(365)),
                Issuer = ApprovalAttestation.Issuer,
                Audience = audience
            },
            key ?? SigningKey);

    /// <summary>Publishes the test key only, as the gateway's key set would.</summary>
    public sealed class Keys : IApprovalKeyResolver
    {
        public Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken) => Task.FromResult(keyId == KeyId ? SigningKey : null);
    }
}
