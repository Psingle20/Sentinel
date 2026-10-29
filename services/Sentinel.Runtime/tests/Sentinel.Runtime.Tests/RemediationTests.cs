using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;

namespace Sentinel.Runtime.Tests;

/// <summary>Phase 06 behaviour on Runtime's side of the control path.</summary>
public sealed class RemediationLifecycleTests
{
    [Fact]
    public void A_revoked_approval_hands_the_incident_to_responders_and_no_start_can_be_confirmed()
    {
        (Incident incident, ProposedPlan plan) = Approved();
        ExecutionRequested requested = incident.RequestExecution("key", Start.AddSeconds(230)).Value;

        incident.RevokeApproval(plan.PlanId, plan.Version, "responder@example.test", "The window closed.", Start.AddSeconds(231)).IsSuccess.ShouldBeTrue();

        incident.State.ShouldBe(IncidentState.Escalated);
        incident.Approval.ShouldBeNull();
        incident.ConfirmStart(plan.PlanId, plan.Version, requested.FencingToken, Start.AddSeconds(232)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void An_approval_cannot_be_revoked_after_execution_started()
    {
        (Incident incident, ProposedPlan plan) = Approved();
        ExecutionRequested requested = incident.RequestExecution("key", Start.AddSeconds(230)).Value;
        incident.ConfirmStart(plan.PlanId, plan.Version, requested.FencingToken, Start.AddSeconds(231));

        incident.RevokeApproval(plan.PlanId, plan.Version, "responder@example.test", "Too late.", Start.AddSeconds(232)).IsFailure.ShouldBeTrue();
        incident.State.ShouldBe(IncidentState.Remediating);
    }

    [Fact]
    public void A_suggestion_only_plan_escalates_with_the_suggestion_and_cannot_be_approved()
    {
        (Incident incident, HypothesisId cause) = Diagnosed();
        ProposedPlan plan = Plan(cause, PolicyVerdict.SuggestionOnly) with { Summary = "pause the reporting-worker batch" };

        incident.ProposePlan(plan).IsSuccess.ShouldBeTrue();

        incident.State.ShouldBe(IncidentState.Escalated);
        incident.ClosingReason.ShouldNotBeNull().ShouldContain("pause the reporting-worker batch");
        incident.Approve(plan.PlanId, plan.Version, plan.Hash, "responder@example.test", "jws", Start.AddSeconds(300)).IsFailure.ShouldBeTrue();
    }

    private static (Incident Incident, HypothesisId Cause) Diagnosed()
    {
        (Incident incident, InvestigationId investigation) = Detected(60);
        var cause = HypothesisId.New();
        incident.RecordInvestigation(Report(investigation, [Supporting(cause, 0.8, 0, 50, "a", "b")], Hypothesis(cause, "A cause.")), Start.AddSeconds(200));
        incident.RecordDecision(FalsificationCheck.Decide(incident, DecisionId.New(), Start.AddSeconds(210)));
        return (incident, cause);
    }

    private static (Incident Incident, ProposedPlan Plan) Approved()
    {
        (Incident incident, HypothesisId cause) = Diagnosed();
        ProposedPlan plan = Plan(cause);
        incident.ProposePlan(plan);
        incident.Approve(plan.PlanId, plan.Version, plan.Hash, "responder@example.test", "jws", Start.AddSeconds(220)).IsSuccess.ShouldBeTrue();
        return (incident, plan);
    }
}

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
            ((IncidentQueryService)provider.GetService(typeof(IncidentQueryService))!).GetAsync(incidentId, CancellationToken.None))).Value;

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
