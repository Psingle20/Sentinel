using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Services;
using Shouldly;

namespace Sentinel.Runtime.Tests;

/// <summary>What Runtime accepts as proof of an approver's decision (ADR-006, review finding F-005).</summary>
public sealed class DecisionVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly IncidentId Incident = IncidentId.From(Guid.NewGuid());
    private static readonly PlanId Plan = PlanId.New();
    private const string Approver = "responder@example.test";

    private readonly DecisionVerifier _verifier = new(new TestDecisions.Keys(), new FakeTimeProvider(Now));

    [Fact]
    public async Task A_rejection_signed_for_Runtime_with_its_scope_verifies()
    {
        string token = TestDecisions.Reject(Incident, Plan, 2, Approver, Now);

        (await _verifier.DecisionProblemAsync(token, ApprovalAttestation.RejectPlanScope, Incident, Plan, 2, Approver, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(ApprovalAttestation.RevokeApprovalScope, ApprovalAttestation.RuntimeAudience, "Scope revoke-approval is not reject-plan.")]
    [InlineData(ApprovalAttestation.RejectPlanScope, ApprovalAttestation.Audience, "Audience sentinel-executor is not sentinel-runtime.")]
    public async Task A_decision_for_another_scope_or_audience_is_refused(string scope, string audience, string problem)
    {
        string token = TestDecisions.Sign(scope, audience, Incident, Plan, 2, "sha256:plan", Approver, Now);

        (await _verifier.DecisionProblemAsync(token, ApprovalAttestation.RejectPlanScope, Incident, Plan, 2, Approver, CancellationToken.None)).ShouldBe(problem);
    }

    [Fact]
    public async Task An_expired_decision_or_one_for_another_plan_version_or_incident_is_refused()
    {
        string expired = TestDecisions.Sign(ApprovalAttestation.RejectPlanScope, ApprovalAttestation.RuntimeAudience, Incident, Plan, 2, "sha256:plan", Approver, Now.AddHours(-2), lifetime: TimeSpan.FromHours(1));
        string current = TestDecisions.Reject(Incident, Plan, 2, Approver, Now);

        (await _verifier.DecisionProblemAsync(expired, ApprovalAttestation.RejectPlanScope, Incident, Plan, 2, Approver, CancellationToken.None)).ShouldNotBeNull().ShouldContain("expired");
        (await _verifier.DecisionProblemAsync(current, ApprovalAttestation.RejectPlanScope, Incident, Plan, 3, Approver, CancellationToken.None)).ShouldBe("The attestation is for another plan version.");
        (await _verifier.DecisionProblemAsync(current, ApprovalAttestation.RejectPlanScope, IncidentId.From(Guid.NewGuid()), Plan, 2, Approver, CancellationToken.None)).ShouldBe("The attestation is for another incident.");
    }

    [Fact]
    public async Task An_approval_must_bind_the_plans_content_hash()
    {
        string token = TestDecisions.Approve(Incident, Plan, 2, "sha256:approved-content", Approver, Now);

        (await _verifier.ApprovalProblemAsync(token, Incident, Plan, 2, "sha256:approved-content", Approver, CancellationToken.None)).ShouldBeNull();
        (await _verifier.ApprovalProblemAsync(token, Incident, Plan, 2, "sha256:other-content", Approver, CancellationToken.None)).ShouldBe("The attestation is for different plan content.");
    }

    [Fact]
    public async Task A_token_that_is_not_a_compact_JWS_is_refused()
    {
        (await _verifier.DecisionProblemAsync("header.claims.signature", ApprovalAttestation.RejectPlanScope, Incident, Plan, 2, Approver, CancellationToken.None)).ShouldNotBeNull();
    }
}
