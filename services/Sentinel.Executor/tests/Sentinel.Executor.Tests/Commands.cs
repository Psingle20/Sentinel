using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Abstractions.Providers;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Application.Abstractions.Security;

namespace Sentinel.Executor.Tests;

internal static class Commands
{
    public static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 10, 0, TimeSpan.Zero);
    public static readonly TenantId Tenant = TenantId.From("acme");
    public static readonly EnvironmentId Environment = EnvironmentId.From("reference");

    public static RemediationPlan Plan(string target = "checkout-service")
    {
        var draft = new RemediationPlan
        {
            PlanId = PlanId.New(),
            PlanVersion = 1,
            PlanHash = string.Empty,
            HypothesisId = HypothesisId.New(),
            Actions = [new RollbackAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From(target), FromRevision = "v2.4.1", ToRevision = "v2.4.0" }],
            ExpectedRecovery = [],
            ObservationWindow = TimeSpan.FromSeconds(90),
            Risk = RiskLevel.Medium
        };
        return draft with { PlanHash = RemediationPlanHash.Compute(draft) };
    }

    /// <summary>Pauses the reporting batch; with <paramref name="alsoRollback"/>, then rolls checkout back.</summary>
    public static RemediationPlan PausePlan(bool alsoRollback = false)
    {
        RemediationAction pause = new PauseBatchAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("reporting-worker"), Reason = "Saturates Redis." };
        RemediationAction rollback = new RollbackAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("checkout-service"), FromRevision = "v2.4.1", ToRevision = "v2.4.0" };
        return Hashed(alsoRollback ? [pause, rollback] : [pause], RiskLevel.Low);
    }

    public static RemediationPlan RollbackThenPause() => Hashed(
    [
        new RollbackAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("checkout-service"), FromRevision = "v2.4.1", ToRevision = "v2.4.0" },
        new PauseBatchAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("reporting-worker"), Reason = "Saturates Redis." }
    ], RiskLevel.Low);

    private static RemediationPlan Hashed(IReadOnlyList<RemediationAction> actions, RiskLevel risk)
    {
        var draft = new RemediationPlan
        {
            PlanId = PlanId.New(),
            PlanVersion = 1,
            PlanHash = string.Empty,
            HypothesisId = HypothesisId.New(),
            Actions = actions,
            ExpectedRecovery = [],
            ObservationWindow = TimeSpan.FromSeconds(90),
            Risk = risk
        };
        return draft with { PlanHash = RemediationPlanHash.Compute(draft) };
    }

    public static ApprovalAttestationClaims Claims(IncidentId incident, RemediationPlan plan) => new()
    {
        AttestationId = AttestationId.New(),
        TenantId = Tenant,
        EnvironmentId = Environment,
        IncidentId = incident,
        PlanId = plan.PlanId,
        PlanVersion = plan.PlanVersion,
        PlanHash = plan.PlanHash,
        ApproverId = "responder@example.test",
        ApprovalScope = ApprovalAttestation.ExecutePlanScope,
        PolicyVersion = "static-2026.10.1",
        IssuedAt = Now.AddMinutes(-1),
        ExpiresAt = Now.AddMinutes(14),
        Issuer = ApprovalAttestation.Issuer,
        Audience = ApprovalAttestation.Audience
    };

    public static ExecuteRemediation Execute(TestSigner signer, RemediationPlan? plan = null, long fencingToken = 7, string? key = null, IncidentId? incident = null, Func<ApprovalAttestationClaims, ApprovalAttestationClaims>? claims = null)
    {
        RemediationPlan approved = plan ?? Plan();
        IncidentId incidentId = incident ?? IncidentId.New();
        ApprovalAttestationClaims signed = Claims(incidentId, approved);
        return new ExecuteRemediation
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = incidentId,
            Plan = approved,
            Attestation = signer.Sign(claims is null ? signed : claims(signed)),
            FencingToken = fencingToken,
            IdempotencyKey = key ?? $"acme:{incidentId}:execute:1",
            RequestedAt = Now
        };
    }
}
