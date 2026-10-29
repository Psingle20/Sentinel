using Reference.DeployController.Domain.Revisions;
using Reference.DeployController.Domain.Revisions.Events;
using Shouldly;

namespace Reference.Tests;

/// <summary>The deploy controller's revision rules, without the services it switches.</summary>
public sealed class DeploymentLedgerRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    private static DeploymentLedger Ledger() => new([new ManagedRevisions("checkout-service", "v2.4.0", ["v2.4.0", "v2.4.1"])]);

    [Fact]
    public void A_rollout_finishes_once_and_raises_what_happened()
    {
        DeploymentLedger ledger = Ledger();
        DeploymentRecord started = ledger.Start("checkout-service", DeploymentKind.Deploy, "v2.4.1", "v2.4.0", "test", null, null, "key-1", Now).Value;

        DeploymentRecord? failed = ledger.Finish(started.DeploymentId, "the service did not answer", Now.AddSeconds(20));
        DeploymentRecord? again = ledger.Finish(started.DeploymentId, null, Now.AddSeconds(30));

        failed.ShouldNotBeNull().Status.ShouldBe(DeploymentStatus.Failed);
        again.ShouldBeNull();
        ledger.Find(started.DeploymentId)!.Status.ShouldBe(DeploymentStatus.Failed);
        ledger.DequeueDomainEvents().Select(raised => raised.GetType()).ShouldBe([typeof(DeploymentStarted), typeof(DeploymentFinished)]);
    }

    [Fact]
    public void One_rollout_per_service_and_only_managed_revisions()
    {
        DeploymentLedger ledger = Ledger();
        ledger.Start("checkout-service", DeploymentKind.Deploy, "v2.4.1", "v2.4.0", "test", null, null, null, Now);

        ledger.Start("checkout-service", DeploymentKind.Rollback, "v2.4.0", "v2.4.0", "test", null, null, null, Now).Error.ShouldBe(DeploymentErrors.Conflict);
        ledger.Start("checkout-service", DeploymentKind.Deploy, "v9.9.9", "v2.4.0", "test", null, null, null, Now).Error.Code.ShouldBe("Deployment.UnknownRevision");
    }

    [Fact]
    public void A_service_already_at_its_baseline_is_not_reset()
    {
        DeploymentLedger ledger = Ledger();

        ledger.Reset("checkout-service", "v2.4.0", Now).ShouldBeNull();
        ledger.Reset("checkout-service", "v2.4.1", Now).ShouldNotBeNull().Kind.ShouldBe(DeploymentKind.Reset);
    }

    [Theory]
    [InlineData(DeploymentStatus.InProgress, DeploymentStatus.Completed, true)]
    [InlineData(DeploymentStatus.InProgress, DeploymentStatus.Failed, true)]
    [InlineData(DeploymentStatus.Completed, DeploymentStatus.Failed, false)]
    [InlineData(DeploymentStatus.Failed, DeploymentStatus.Completed, false)]
    public void A_deployment_moves_only_as_the_table_allows(DeploymentStatus from, DeploymentStatus to, bool allowed) =>
        DeploymentStatusTransitions.CanMove(from, to).ShouldBe(allowed);
}
