using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Api.Application.Approvals;
using Sentinel.Api.Domain.Approvals;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Shouldly;

namespace Sentinel.Api.Tests;

public sealed class ApprovalServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 8, 0, TimeSpan.Zero);

    private readonly FakeIncidents _incidents = new();
    private readonly CapturingSigner _signer = new();
    private readonly InMemoryApprovals _approvals = new();
    private readonly RecordingOutbox _outbox = new();

    private ApprovalService Service() => new(
        _incidents,
        _signer,
        _approvals,
        _outbox,
        new NoUnitOfWork(),
        new FakeTimeProvider(Now),
        Microsoft.Extensions.Options.Options.Create(new ApprovalOptions { AttestationLifetime = TimeSpan.FromMinutes(15) }),
        NullLogger<ApprovalService>.Instance);

    [Fact]
    public async Task Approving_the_current_plan_signs_a_short_lived_attestation_bound_to_it_and_relays_it_to_Runtime()
    {
        RemediationPlan plan = _incidents.Plan;

        CleanArchitecture.BuildingBlocks.Result<ApprovalReceipt> result = await Service().ApproveAsync(
            new ApprovePlan(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "responder@example.test"), CancellationToken.None);

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
        var command = new ApprovePlan(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "responder@example.test");

        ApprovalReceipt first = (await Service().ApproveAsync(command, CancellationToken.None)).Value;
        ApprovalReceipt second = (await Service().ApproveAsync(command, CancellationToken.None)).Value;

        second.ShouldBe(first);
        _signer.Signed.Count.ShouldBe(1);
        _outbox.Messages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_changed_hash_another_version_or_a_plan_not_awaiting_approval_is_refused()
    {
        RemediationPlan plan = _incidents.Plan;

        (await Service().ApproveAsync(new ApprovePlan(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, "sha256:stale", "r@example.test"), CancellationToken.None))
            .Error.ShouldBe(ApprovalErrors.HashMismatch);
        (await Service().ApproveAsync(new ApprovePlan(_incidents.IncidentId, plan.PlanId, plan.PlanVersion + 1, plan.PlanHash, "r@example.test"), CancellationToken.None))
            .Error.ShouldBe(ApprovalErrors.NotCurrentPlan);

        _incidents.Status = PlanStatus.Executing;
        (await Service().ApproveAsync(new ApprovePlan(_incidents.IncidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "r@example.test"), CancellationToken.None))
            .Error.Code.ShouldBe("Approval.NotAwaitingApproval");
        _signer.Signed.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rejection_needs_a_reason_and_is_relayed_to_Runtime()
    {
        RemediationPlan plan = _incidents.Plan;

        (await Service().RejectAsync(new RejectPlan(_incidents.IncidentId, plan.PlanId, 1, "  ", "r@example.test"), CancellationToken.None))
            .Error.ShouldBe(ApprovalErrors.ReasonRequired);
        (await Service().RejectAsync(new RejectPlan(_incidents.IncidentId, plan.PlanId, 1, "Migration window.", "r@example.test"), CancellationToken.None))
            .Value.Decision.ShouldBe(ApprovalDecision.Rejected);

        _outbox.Messages.OfType<RecordRejection>().ShouldHaveSingleItem().Reason.ShouldBe("Migration window.");
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

    private sealed class CapturingSigner : IApprovalSigner
    {
        public List<ApprovalAttestationClaims> Signed { get; } = [];

        public Task<SignedAttestation> SignAsync(ApprovalAttestationClaims claims, CancellationToken cancellationToken)
        {
            Signed.Add(claims);
            return Task.FromResult(new SignedAttestation("signed-token", "test-key"));
        }

        public Task<ApprovalKeySet> PublishedKeysAsync(CancellationToken cancellationToken) => Task.FromResult(new ApprovalKeySet([]));
    }

    private sealed class InMemoryApprovals : IApprovalStore
    {
        private readonly List<ApprovalRecord> _records = [];

        public Task<ApprovalRecord?> FindAsync(IncidentId incidentId, PlanId planId, int planVersion, CancellationToken cancellationToken) =>
            Task.FromResult(_records.Find(record => record.IncidentId == incidentId && record.PlanId == planId && record.PlanVersion == planVersion));

        public Task SaveAsync(ApprovalRecord record, CancellationToken cancellationToken)
        {
            _records.Add(record);
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
