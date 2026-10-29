using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.Abstractions.Security;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;
using Sentinel.Gateway.Application.BusinessTasks.Commands.Approvals;
using Sentinel.Gateway.Application.Options;
using Sentinel.Gateway.Domain.Approvals;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Shouldly;

namespace Sentinel.Gateway.Tests.BusinessTasks.Commands.Approvals;

/// <summary>The approval handlers: what they sign, keep, relay to Runtime and ask Runtime once the approval rules allow a decision.</summary>
public sealed class ApprovalHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 8, 0, TimeSpan.Zero);

    private readonly FakeIncidents _incidents = new();
    private readonly CapturingSigner _signer = new();
    private readonly InMemoryApprovals _approvals = new();
    private readonly RecordingOutbox _outbox = new();
    private readonly FakeRemediation _runtime = new();

    private DecisionAttestations Attestations() =>
        new(_signer, Microsoft.Extensions.Options.Options.Create(new ApprovalOptions { AttestationLifetime = TimeSpan.FromMinutes(15) }));

    private Task<Result<ApprovalReceipt>> ApproveAsync(ApprovePlanCommand command) => new ApprovePlanCommandHandler(
        _incidents,
        _signer,
        _approvals,
        _outbox,
        new NoUnitOfWork(),
        new FakeTimeProvider(Now),
        Microsoft.Extensions.Options.Options.Create(new ApprovalOptions { AttestationLifetime = TimeSpan.FromMinutes(15) }),
        NullLogger<ApprovePlanCommandHandler>.Instance).Handle(command, CancellationToken.None);

    private Task<Result<ApprovalReceipt>> RejectAsync(RejectPlanCommand command) =>
        new RejectPlanCommandHandler(_incidents, _approvals, Attestations(), _outbox, new NoUnitOfWork(), new FakeTimeProvider(Now)).Handle(command, CancellationToken.None);

    private Task<Result<ApprovalReceipt>> RevokeAsync(RevokeApprovalCommand command) =>
        new RevokeApprovalCommandHandler(_incidents, _runtime, _approvals, Attestations(), new NoUnitOfWork(), new FakeTimeProvider(Now), NullLogger<RevokeApprovalCommandHandler>.Instance).Handle(command, CancellationToken.None);

    [Fact]
    public async Task Approving_the_current_plan_signs_a_short_lived_attestation_bound_to_it_and_relays_it_to_Runtime()
    {
        RemediationPlan plan = _incidents.Plan;

        Result<ApprovalReceipt> result = await ApproveAsync(new ApprovePlanCommand(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "responder@example.test"));

        result.IsSuccess.ShouldBeTrue();
        ApprovalAttestationClaims claims = _signer.Signed.ShouldHaveSingleItem();
        claims.PlanHash.ShouldBe(plan.PlanHash);
        claims.Audience.ShouldBe(ApprovalAttestation.Audience);
        claims.ApprovalScope.ShouldBe(ApprovalAttestation.ExecutePlanScope);
        claims.PolicyVersion.ShouldBe("static-2026.10.1");
        claims.ExpiresAt.ShouldBe(Now.AddMinutes(15));

        RecordApproval relayed = _outbox.Messages.OfType<RecordApproval>().ShouldHaveSingleItem();
        relayed.Attestation.ShouldBe("signed-token");
        relayed.PlanHash.ShouldBe(plan.PlanHash);
    }

    [Fact]
    public async Task Approving_again_returns_the_same_receipt_without_a_second_attestation()
    {
        RemediationPlan plan = _incidents.Plan;
        var command = new ApprovePlanCommand(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "responder@example.test");

        ApprovalReceipt first = (await ApproveAsync(command)).Value;
        ApprovalReceipt second = (await ApproveAsync(command)).Value;

        second.ShouldBe(first);
        _signer.Signed.Count.ShouldBe(1);
        _outbox.Messages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_changed_hash_another_version_or_a_plan_not_awaiting_approval_is_refused()
    {
        RemediationPlan plan = _incidents.Plan;

        (await ApproveAsync(new ApprovePlanCommand(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, "sha256:stale", "r@example.test")))
            .Error.ShouldBe(ApprovalErrors.HashMismatch);
        (await ApproveAsync(new ApprovePlanCommand(_incidents.IncidentId, plan.PlanId, plan.PlanVersion + 1, plan.PlanHash, "r@example.test")))
            .Error.ShouldBe(ApprovalErrors.NotCurrentPlan);

        _incidents.Status = PlanStatus.Executing;
        (await ApproveAsync(new ApprovePlanCommand(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "r@example.test")))
            .Error.Code.ShouldBe("Approval.NotAwaitingApproval");
        _signer.Signed.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rejection_needs_a_reason_and_is_relayed_to_Runtime_under_a_signed_attestation()
    {
        RemediationPlan plan = _incidents.Plan;

        (await RejectAsync(new RejectPlanCommand(_incidents.IncidentId, plan.PlanId, 1, "  ", "r@example.test")))
            .Error.ShouldBe(ApprovalErrors.ReasonRequired);
        (await RejectAsync(new RejectPlanCommand(_incidents.IncidentId, plan.PlanId, 1, "Migration window.", "r@example.test")))
            .Value.Decision.ShouldBe(ApprovalDecision.Rejected);

        RecordRejection relayed = _outbox.Messages.OfType<RecordRejection>().ShouldHaveSingleItem();
        relayed.Reason.ShouldBe("Migration window.");
        relayed.Attestation.ShouldBe("signed-token");
        ApprovalAttestationClaims claims = _signer.Signed.ShouldHaveSingleItem();
        claims.ApprovalScope.ShouldBe(ApprovalAttestation.RejectPlanScope);
        claims.Audience.ShouldBe(ApprovalAttestation.RuntimeAudience);
        claims.PlanHash.ShouldBe(plan.PlanHash);
        claims.ApproverId.ShouldBe("r@example.test");
    }

    [Fact]
    public async Task A_revocation_is_recorded_only_once_Runtime_accepted_it_and_carries_a_signed_attestation()
    {
        // Review F-003: the Gateway's record must never say "revoked" while Runtime, the authority, went ahead.
        RemediationPlan plan = _incidents.Plan;
        await ApproveAsync(new ApprovePlanCommand(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "r@example.test"));

        Result<ApprovalReceipt> revoked = await RevokeAsync(new RevokeApprovalCommand(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, "Wrong target.", "r@example.test"));

        revoked.Value.Decision.ShouldBe(ApprovalDecision.Revoked);
        RecordRevocation asked = _runtime.Revocations.ShouldHaveSingleItem();
        asked.Attestation.ShouldBe("signed-token");
        _signer.Signed[^1].ApprovalScope.ShouldBe(ApprovalAttestation.RevokeApprovalScope);
        _signer.Signed[^1].Audience.ShouldBe(ApprovalAttestation.RuntimeAudience);
        _outbox.Messages.OfType<RecordRevocation>().ShouldBeEmpty();
        (await _approvals.FindAsync(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, CancellationToken.None))!.Decision.ShouldBe(ApprovalDecision.Revoked);
    }

    [Fact]
    public async Task A_revocation_Runtime_refuses_leaves_the_approval_standing_and_reports_why()
    {
        RemediationPlan plan = _incidents.Plan;
        await ApproveAsync(new ApprovePlanCommand(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "r@example.test"));
        _runtime.Answer = new RevocationResponse(false, "Execution already started.");

        Result<ApprovalReceipt> refused = await RevokeAsync(new RevokeApprovalCommand(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, "Too late.", "r@example.test"));

        refused.Error.ShouldBe(ApprovalErrors.ExecutionStarted);
        (await _approvals.FindAsync(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, CancellationToken.None))!.Decision.ShouldBe(ApprovalDecision.Approved);
    }

    [Fact]
    public async Task A_revocation_when_Runtime_cannot_be_reached_fails_closed_and_changes_nothing()
    {
        RemediationPlan plan = _incidents.Plan;
        await ApproveAsync(new ApprovePlanCommand(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "r@example.test"));
        _runtime.Unreachable = true;

        Result<ApprovalReceipt> failed = await RevokeAsync(new RevokeApprovalCommand(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, "Wrong target.", "r@example.test"));

        failed.Error.ShouldBe(ApprovalErrors.RuntimeUnavailable);
        (await _approvals.FindAsync(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, CancellationToken.None))!.Decision.ShouldBe(ApprovalDecision.Approved);
    }

    private sealed class FakeIncidents : IRuntimeIncidents
    {
        public FakeIncidents()
        {
            var draft = new RemediationPlan
            {
                PlanId = PlanId.New(),
                PlanVersion = 1,
                PlanHash = string.Empty,
                HypothesisId = HypothesisId.New(),
                Actions = [new RollbackAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("checkout-service"), FromRevision = "v2.4.1", ToRevision = "v2.4.0" }],
                ExpectedRecovery = [],
                ObservationWindow = TimeSpan.FromSeconds(90),
                Risk = RiskLevel.Medium
            };
            Plan = draft with { PlanHash = RemediationPlanHash.Compute(draft) };
        }

        public IncidentId IncidentId { get; } = IncidentId.New();

        public RemediationPlan Plan { get; }

        public PlanStatus Status { get; set; } = PlanStatus.AwaitingApproval;

        public Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IncidentSummary>>([]);

        public Task<IncidentDetail?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken)
        {
            if (incidentId != IncidentId)
            {
                return Task.FromResult<IncidentDetail?>(null);
            }

            return Task.FromResult<IncidentDetail?>(new IncidentDetail
            {
                Summary = new IncidentSummary
                {
                    TenantId = TenantId.From("acme"),
                    EnvironmentId = EnvironmentId.From("reference"),
                    IncidentId = IncidentId,
                    AffectedServiceId = ServiceId.From("checkout-service"),
                    Title = "checkout burn",
                    Severity = Severity.Critical,
                    Status = Status == PlanStatus.AwaitingApproval ? IncidentStatus.AwaitingApproval : IncidentStatus.Remediating,
                    OpenedAt = Now,
                    UpdatedAt = Now,
                    StreamVersion = 6
                },
                Plan = new PlanView(Plan, new PolicyEvaluation(PolicyDecision.ApprovalRequired, "static-2026.10.1", ["rule"], Now), Status, null, null)
            });
        }

        public Task<IncidentRunRecord?> RunRecordAsync(IncidentId incidentId, CancellationToken cancellationToken) => Task.FromResult<IncidentRunRecord?>(null);
    }

    private sealed class FakeRemediation : IRuntimeRemediation
    {
        public List<RecordRevocation> Revocations { get; } = [];

        public RevocationResponse Answer { get; set; } = new(true, null);

        public bool Unreachable { get; set; }

        public Task<RevocationResponse> RevokeAsync(RecordRevocation revocation, CancellationToken cancellationToken)
        {
            if (Unreachable)
            {
                throw new HttpRequestException("Runtime is down.");
            }

            if (Answer.Accepted)
            {
                Revocations.Add(revocation);
            }

            return Task.FromResult(Answer);
        }
    }

    private sealed class CapturingSigner : IApprovalSigner
    {
        public List<ApprovalAttestationClaims> Signed { get; } = [];

        public Task<SignedAttestation> SignAsync(ApprovalAttestationClaims claims, CancellationToken cancellationToken)
        {
            Signed.Add(claims);
            return Task.FromResult(new SignedAttestation("signed-token", "test-key"));
        }

        public Task<SignedAttestation> SignOperatorAsync(OperatorAttestationClaims claims, CancellationToken cancellationToken) =>
            Task.FromResult(new SignedAttestation("operator-token", "test-key"));

        public Task<ApprovalKeySet> PublishedKeysAsync(CancellationToken cancellationToken) => Task.FromResult(new ApprovalKeySet([]));
    }

    private sealed class InMemoryApprovals : IApprovalStore
    {
        private readonly List<ApprovalSnapshot> _records = [];

        // Stored as snapshots, as the PostgreSQL store does, so each find rebuilds the approval.
        public Task<Approval?> FindAsync(IncidentId incidentId, PlanId planId, int planVersion, CancellationToken cancellationToken) =>
            Task.FromResult(_records.FindLast(record => record.IncidentId == incidentId && record.PlanId == planId && record.PlanVersion == planVersion) is { } found ? Approval.Rehydrate(found) : null);

        public Task SaveAsync(Approval approval, CancellationToken cancellationToken)
        {
            _records.Add(approval.Snapshot());
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOutbox : IOutbox
    {
        public List<object> Messages { get; } = [];

        public void Enqueue<TMessage>(TMessage message, MessageMetadata? metadata = null)
            where TMessage : class => Messages.Add(message);
    }

    private sealed class NoUnitOfWork : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
