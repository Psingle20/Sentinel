using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Incidents.Commands;
using Sentinel.Runtime.Domain.Incidents.Events;
using Sentinel.Runtime.Application.BusinessProcesses.Incidents;
using Microsoft.Extensions.DependencyInjection;

namespace Sentinel.Runtime.Tests;

public sealed class RemediationPlanningTests : IDisposable
{
    private readonly InMemoryRuntime _runtime = new();

    public void Dispose() => _runtime.Dispose();

    [Fact]
    public async Task A_rollback_plan_states_its_preconditions_blast_radius_and_compensation_and_carries_the_confidence()
    {
        IncidentId incidentId = await DiagnoseAsync(new HypothesisSpec("change:deploy-controller:deployment:d1:started", ObservationId.From("deploy-controller:deployment:d1:started"), null));

        IncidentSnapshot planned = await _runtime.InScopeAsync(provider => Steps(provider).PlanAsync(incidentId, CancellationToken.None));

        planned.State.ShouldBe(IncidentState.AwaitingApproval);
        RemediationPlan plan = _runtime.Outbox.Of<Sentinel.Contracts.Remediation.RemediationPlanProposed>().Single().Plan;
        plan.Preconditions.ShouldNotBeNull().Single().Expected.ShouldBe("v2.4.1");
        plan.BlastRadius.ShouldNotBeNull().ShouldBe([Checkout]);
        plan.Compensation.ShouldNotBeNull().ShouldContain("human decision");
        _runtime.Policy.Last!.Confidence.ShouldNotBeNull();
        _runtime.Policy.Last.Severity.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_saturating_batch_gets_a_suggested_pause_that_Sentinel_will_not_execute()
    {
        IncidentId incidentId = await DiagnoseAsync(new HypothesisSpec("redis-saturation:reporting-worker", null, Reporting));

        IncidentSnapshot planned = await _runtime.InScopeAsync(provider => Steps(provider).PlanAsync(incidentId, CancellationToken.None));

        planned.State.ShouldBe(IncidentState.Escalated);
        planned.Policy.ShouldBe(PolicyVerdict.SuggestionOnly);
        _runtime.Outbox.Of<Sentinel.Contracts.Remediation.RemediationPlanProposed>().Single().Plan.Actions.Single().ShouldBeOfType<PauseBatchAction>();
        _runtime.Outbox.Of<Sentinel.Contracts.Remediation.ExecuteRemediation>().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_poison_message_gets_a_quarantine_of_messages_received_too_often_that_needs_approval()
    {
        IncidentId incidentId = await DiagnoseAsync(new HypothesisSpec("poison-message:queue-consumer", null, Consumer));

        IncidentSnapshot planned = await _runtime.InScopeAsync(provider => Steps(provider).PlanAsync(incidentId, CancellationToken.None));

        planned.State.ShouldBe(IncidentState.AwaitingApproval);
        RemediationPlan plan = _runtime.Outbox.Of<Sentinel.Contracts.Remediation.RemediationPlanProposed>().Single().Plan;
        QuarantineMessageAction quarantine = plan.Actions.Single().ShouldBeOfType<QuarantineMessageAction>();
        quarantine.TargetServiceId.ShouldBe(Consumer);
        quarantine.Queue.ShouldBe("orders");
        quarantine.MinimumReceives.ShouldBe(3);
        quarantine.MaxMessages.ShouldBe(5);
        plan.Preconditions.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(new PlanPrecondition(Consumer, "queue-state", "poisoned"));
        plan.BlastRadius.ShouldNotBeNull().ShouldBe([Consumer]);
        plan.Compensation.ShouldNotBeNull().ShouldContain("Release");
    }

    [Fact]
    public async Task Signals_worse_after_the_action_than_before_it_require_compensation()
    {
        IncidentId incidentId = await DiagnoseAsync(new HypothesisSpec("change:deploy-controller:deployment:d1:started", ObservationId.From("deploy-controller:deployment:d1:started"), null));
        RemediationPlan plan = await RemediateAsync(incidentId);
        IncidentSnapshot scheduled = await _runtime.InScopeAsync(provider => Steps(provider).ScheduleRecoveryAsync(incidentId, CancellationToken.None));
        _runtime.Queries.Value = 9;
        _runtime.Queries.Before = 4;
        _runtime.Queries.WindowFrom = scheduled.RecoveryWindowFrom;

        await _runtime.InScopeAsync(provider => Steps(provider).VerifyRecoveryAsync(incidentId, final: true, CancellationToken.None));

        IncidentSnapshot judged = (await _runtime.Owner.GetAsync(incidentId, CancellationToken.None))!;
        judged.Recovery.ShouldBe(RecoveryResult.CompensationRequired);
        judged.State.ShouldBe(IncidentState.Escalated);
        plan.PlanId.ShouldBe(judged.PlanId!.Value);

        // Responders see the signal before and after the action (Phase 08).
        Sentinel.Contracts.Incidents.RecoveryMeasurement measured = _runtime.Outbox.Of<Sentinel.Contracts.Incidents.RecoveryEvaluated>().Single().Measurements.Single();
        measured.BaselineValue.ShouldBe(4);
        measured.ObservedValue.ShouldBe(9);
    }

    [Fact]
    public async Task A_plan_that_needs_approval_carries_the_moment_its_request_expires()
    {
        IncidentId incidentId = await DiagnoseAsync(new HypothesisSpec("change:deploy-controller:deployment:d1:started", ObservationId.From("deploy-controller:deployment:d1:started"), null));

        IncidentSnapshot planned = await _runtime.InScopeAsync(provider => Steps(provider).PlanAsync(incidentId, CancellationToken.None));
        Sentinel.Contracts.Incidents.IncidentDetail detail = (await _runtime.InScopeAsync(provider =>
            provider.GetRequiredService<CleanArchitecture.BuildingBlocks.Messaging.IRequestDispatcher>().HandleRequestAsync(new GetIncidentRequest(incidentId), CancellationToken.None))).Value;

        planned.State.ShouldBe(IncidentState.AwaitingApproval);
        detail.Plan.ShouldNotBeNull().ApprovalDeadline.ShouldNotBeNull().ShouldBeGreaterThan(DateTimeOffset.UnixEpoch);
    }

    private static IncidentSteps Steps(IServiceProvider provider) => (IncidentSteps)provider.GetService(typeof(IncidentSteps))!;

    private sealed record HypothesisSpec(string Key, ObservationId? Change, ServiceId? Service);

    private async Task<IncidentId> DiagnoseAsync(HypothesisSpec spec)
    {
        var incidentId = IncidentId.New();
        LinkedChange change = Deployment("checkout-service", "v2.4.1", "v2.4.0", -60, -40, "d1");
        await _runtime.Owner.SendAsync(incidentId, new OpenIncidentCommand(InMemoryRuntime.Tenant, InMemoryRuntime.Environment, Checkout, "checkout-service slo_burn_rate at 9.0", IncidentSeverity.High, Start, [ObservationId.From("prometheus:slo_burn_rate:checkout-service:x")], [change]), CancellationToken.None);
        var investigation = InvestigationId.New();
        await _runtime.Owner.SendAsync(incidentId, new RequestInvestigationsCommand([new InvestigationSpec(investigation, null, "Find it.", Start.AddMinutes(-15), Start.AddMinutes(1))]), CancellationToken.None);
        var cause = HypothesisId.New();
        Hypothesis hypothesis = Hypothesis(cause, "The cause.", spec.Service, spec.Change) with { Key = spec.Key };
        Evidence[] evidence =
        [
            Supporting(cause, 0.85, -30, 0, "surge-1", "surge-2") with { IndependenceGroup = "redis-connections" },
            Supporting(cause, 0.6, -40, -20, "deploy-1", "deploy-2") with { IndependenceGroup = "deployment" }
        ];
        await _runtime.Owner.SendAsync(incidentId, new RecordInvestigationCommand(Report(investigation, evidence, hypothesis, Hypothesis(HypothesisId.New(), "Load."))), CancellationToken.None);
        await _runtime.Owner.SendAsync(incidentId, new RecordDecisionCommand(FalsificationCheckFor(incidentId)), CancellationToken.None);
        return incidentId;
    }

    private Decision FalsificationCheckFor(IncidentId incidentId)
    {
        IReadOnlyList<StoredEvent> history = _runtime.Store.ReadStreamAsync(incidentId, CancellationToken.None).GetAwaiter().GetResult();
        Decision decision = FalsificationCheck.Decide(Incident.Rehydrate(history.Select(stored => stored.Event)), DecisionId.New(), Start.AddMinutes(2));
        decision.Result.ShouldBe(DecisionResult.Diagnosed);
        return decision;
    }

    private async Task<RemediationPlan> RemediateAsync(IncidentId incidentId)
    {
        await _runtime.InScopeAsync(provider => Steps(provider).PlanAsync(incidentId, CancellationToken.None));
        RemediationPlan plan = _runtime.Outbox.Of<Sentinel.Contracts.Remediation.RemediationPlanProposed>().Single().Plan;
        await _runtime.Owner.SendAsync(incidentId, new ApprovePlanCommand(plan.PlanId, plan.PlanVersion, plan.PlanHash, "responder@example.test", "jws"), CancellationToken.None);
        await _runtime.InScopeAsync(provider => Steps(provider).RequestExecutionAsync(incidentId, CancellationToken.None));
        long token = _runtime.Outbox.Of<Sentinel.Contracts.Remediation.ExecuteRemediation>().Single().FencingToken;
        await _runtime.Owner.SendAsync(incidentId, new ConfirmStartCommand(InMemoryRuntime.Tenant, InMemoryRuntime.Environment, plan.PlanId, plan.PlanVersion, token), CancellationToken.None);
        await _runtime.Owner.SendAsync(incidentId, new RecordExecutionCommand(plan.PlanId, plan.PlanVersion, ExecutionResult.ActionVerified, "Rolled back."), CancellationToken.None);
        return plan;
    }
}
