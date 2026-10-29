using Sentinel.Gateway.Domain.Approvals;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;

namespace Sentinel.Gateway.Tests.Domain;

/// <summary>The approval rules (ADR-006), which decide every outcome the approval endpoints report.</summary>
public sealed class ApprovalTests
{
    private const string Hash = "sha256:reviewed";
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 30, 0, TimeSpan.Zero);
    private static readonly PlanId Plan = PlanId.New();
    private static readonly PlanReference Reviewed = new(Plan, 2);

    [Fact]
    public void Only_the_current_version_awaiting_approval_with_the_reviewed_content_may_be_approved()
    {
        Approval.CheckApproval(null, Incident(), Reviewed, Hash).Value.ShouldBeNull();

        Approval.CheckApproval(null, null, Reviewed, Hash).Error.ShouldBe(ApprovalErrors.IncidentNotFound);
        Approval.CheckApproval(null, Incident(plan: null), Reviewed, Hash).Error.ShouldBe(ApprovalErrors.NotCurrentPlan);
        Approval.CheckApproval(null, Incident(), Reviewed with { PlanVersion = 3 }, Hash).Error.ShouldBe(ApprovalErrors.NotCurrentPlan);
        Approval.CheckApproval(null, Incident(), Reviewed, "sha256:stale").Error.ShouldBe(ApprovalErrors.HashMismatch);
        Approval.CheckApproval(null, Incident(Current() with { ContentMatchesHash = false }), Reviewed, Hash).Error.ShouldBe(ApprovalErrors.HashMismatch);
        Approval.CheckApproval(null, Incident(Current() with { Status = "Executing", AwaitingApproval = false }), Reviewed, Hash).Error
            .ShouldBe(ApprovalErrors.NotAwaitingApproval("Executing"));
        Approval.CheckApproval(null, Incident() with { AwaitingApproval = false }, Reviewed, Hash).Error.Code.ShouldBe("Approval.NotAwaitingApproval");
    }

    [Fact]
    public void An_approval_is_bound_to_the_plan_and_its_attestation()
    {
        var attestation = new IssuedAttestation(AttestationId.New(), "key-1", Now.AddMinutes(15));

        var approval = Approval.Approve(Incident(), "approver@example.test", attestation, Now);

        approval.Decision.ShouldBe(ApprovalDecision.Approved);
        approval.PlanId.ShouldBe(Plan);
        approval.PlanVersion.ShouldBe(2);
        approval.PlanHash.ShouldBe(Hash);
        approval.AttestationId.ShouldBe(attestation.AttestationId);
        approval.ExpiresAt.ShouldBe(attestation.ExpiresAt);
        approval.Snapshot().KeyId.ShouldBe("key-1");
    }

    [Fact]
    public void One_decision_per_version_the_same_one_again_is_the_existing_approval()
    {
        var approved = Approval.Approve(Incident(), "approver@example.test", new IssuedAttestation(AttestationId.New(), "key-1", Now.AddMinutes(15)), Now);
        var rejected = Approval.Reject(Incident(), "approver@example.test", "Outside the change window.", Now);

        Approval.CheckApproval(approved, Incident(), Reviewed, Hash).Value.ShouldBeSameAs(approved);
        Approval.CheckApproval(rejected, Incident(), Reviewed, Hash).Error.ShouldBe(ApprovalErrors.AlreadyDecided);
        Approval.CheckRejection(rejected, Incident(), Reviewed, "again").Value.ShouldBeSameAs(rejected);
        Approval.CheckRejection(approved, Incident(), Reviewed, "again").Error.ShouldBe(ApprovalErrors.AlreadyDecided);
    }

    [Fact]
    public void A_rejection_needs_a_reason_before_anything_else()
    {
        Approval.CheckRejection(null, null, Reviewed, "  ").Error.ShouldBe(ApprovalErrors.ReasonRequired);
        Approval.CheckRejection(null, null, Reviewed, "Outside the change window.").Error.ShouldBe(ApprovalErrors.IncidentNotFound);

        var rejected = Approval.Reject(Incident(), "approver@example.test", "Outside the change window.", Now);
        rejected.Decision.ShouldBe(ApprovalDecision.Rejected);
        rejected.Reason.ShouldBe("Outside the change window.");
        rejected.AttestationId.ShouldBeNull();
    }

    [Fact]
    public void Only_an_approval_whose_execution_has_not_started_can_be_revoked_and_only_once()
    {
        var approved = Approval.Approve(Incident(), "approver@example.test", new IssuedAttestation(AttestationId.New(), "key-1", Now.AddMinutes(15)), Now);
        var rejected = Approval.Reject(Incident(), "approver@example.test", "No.", Now);

        Approval.CheckRevocation(approved, Incident(), " ").Error.ShouldBe(ApprovalErrors.ReasonRequired);
        Approval.CheckRevocation(null, Incident(), "Wrong target.").Error.ShouldBe(ApprovalErrors.NothingToRevoke);
        Approval.CheckRevocation(rejected, Incident(), "Wrong target.").Error.ShouldBe(ApprovalErrors.NothingToRevoke);
        Approval.CheckRevocation(approved, null, "Wrong target.").Error.ShouldBe(ApprovalErrors.IncidentNotFound);
        Approval.CheckRevocation(approved, Incident() with { ExecutionStarted = true }, "Wrong target.").Error.ShouldBe(ApprovalErrors.ExecutionStarted);
        Approval.CheckRevocation(approved, Incident(), "Wrong target.").Value.ShouldBeNull();

        approved.Revoke("approver@example.test", "Wrong target.", Now.AddMinutes(1));

        approved.Decision.ShouldBe(ApprovalDecision.Revoked);
        approved.Snapshot().RevokedAt.ShouldBe(Now.AddMinutes(1));
        Approval.CheckRevocation(approved, Incident() with { ExecutionStarted = true }, "Again.").Value.ShouldBeSameAs(approved);
        Should.Throw<InvalidOperationException>(() => approved.Revoke("approver@example.test", "Again.", Now));
    }

    [Fact]
    public void An_approval_rebuilt_from_its_snapshot_is_the_same_approval()
    {
        var approved = Approval.Approve(Incident(), "approver@example.test", new IssuedAttestation(AttestationId.New(), "key-1", Now.AddMinutes(15)), Now);

        Approval.Rehydrate(approved.Snapshot()).Snapshot().ShouldBe(approved.Snapshot());
    }

    private static CurrentPlan Current() => new(Plan, 2, Hash, ContentMatchesHash: true, Status: "AwaitingApproval", AwaitingApproval: true);

    private static IncidentState Incident() => Incident(Current());

    private static IncidentState Incident(CurrentPlan? plan) =>
        new(TenantId.From("acme"), EnvironmentId.From("reference"), IncidentId.New(), plan, AwaitingApproval: true, ExecutionStarted: false);
}
