using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Domain.Incidents.Events;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;

namespace Sentinel.Runtime.Tests;

/// <summary>The incident lifecycle tables: every state change the aggregate makes is one the table allows, and operations run only where it lists them.</summary>
public sealed class IncidentStateMachineTests
{
    private static readonly DateTimeOffset Later = Start.AddMinutes(5);

    [Fact]
    public void Each_operation_runs_only_in_the_states_its_table_lists()
    {
        IncidentStateMachine.Allows(IncidentState.AwaitingApproval, IncidentOperation.Approve).ShouldBeTrue();
        IncidentStateMachine.Allows(IncidentState.Escalated, IncidentOperation.Approve).ShouldBeFalse();
        IncidentStateMachine.Allows(IncidentState.Resolved, IncidentOperation.Reopen).ShouldBeTrue();
        IncidentStateMachine.Allows(IncidentState.Escalated, IncidentOperation.Reopen).ShouldBeFalse();
        IncidentStateMachine.Allows(IncidentState.Escalated, IncidentOperation.Escalate).ShouldBeFalse();
        IncidentStateMachine.Allows(IncidentState.Resolved, IncidentOperation.LinkSignal).ShouldBeFalse();
        IncidentStateMachine.Allows(IncidentState.Remediating, IncidentOperation.ConfirmStart).ShouldBeTrue();
        IncidentStateMachine.IsOpen(IncidentState.Escalated).ShouldBeTrue();
        IncidentStateMachine.IsOpen(IncidentState.Resolved).ShouldBeFalse();
    }

    [Fact]
    public void Every_state_change_on_the_path_to_resolution_is_in_the_transition_table()
    {
        var states = new List<IncidentState>();
        Incident incident = AwaitingApproval(out ProposedPlan plan, states);

        Record(states, incident, () => incident.Approve(plan.PlanId, plan.Version, plan.Hash, "responder@example.test", "jws", Later));
        ExecutionRequested execution = incident.RequestExecution("key", Later).Value;
        states.Add(incident.State);
        Record(states, incident, () => incident.ConfirmStart(plan.PlanId, plan.Version, execution.FencingToken, Later));
        Record(states, incident, () => incident.RecordExecution(plan.PlanId, plan.Version, ExecutionResult.ActionVerified, "serving v2.4.0", Later));
        incident.ScheduleRecovery(Later).IsSuccess.ShouldBeTrue();
        states.Add(incident.State);
        Record(states, incident, () => incident.RecordRecovery(RecoveryResult.Recovered, [], Later, null, Later.AddMinutes(2)));
        Record(states, incident, () => incident.Reopen("The symptom returned.", [], Later.AddMinutes(3), Later.AddMinutes(3)));

        states[^1].ShouldBe(IncidentState.Detected);
        states.Zip(states.Skip(1)).ShouldAllBe(step => IncidentStateMachine.CanMove(step.First, step.Second));
    }

    [Fact]
    public void A_follow_up_after_an_unrecovered_remedy_is_in_the_transition_table()
    {
        var states = new List<IncidentState>();
        Incident incident = AwaitingApproval(out ProposedPlan plan, states);

        Record(states, incident, () => incident.Approve(plan.PlanId, plan.Version, plan.Hash, "responder@example.test", "jws", Later));
        ExecutionRequested execution = incident.RequestExecution("key", Later).Value;
        states.Add(incident.State);
        Record(states, incident, () => incident.ConfirmStart(plan.PlanId, plan.Version, execution.FencingToken, Later));
        Record(states, incident, () => incident.RecordExecution(plan.PlanId, plan.Version, ExecutionResult.ActionVerified, "serving v2.4.0", Later));
        incident.ScheduleRecovery(Later).IsSuccess.ShouldBeTrue();
        states.Add(incident.State);
        Record(states, incident, () => incident.RecordRecovery(RecoveryResult.Unchanged, [], Later.AddMinutes(1), "slo_burn_rate=6.4", Later.AddMinutes(2), followUp: true));

        states[^2..].ShouldBe([IncidentState.Verifying, IncidentState.Detected]);
        states.Zip(states.Skip(1)).ShouldAllBe(step => IncidentStateMachine.CanMove(step.First, step.Second));
    }

    [Fact]
    public void Rejection_and_revocation_escalate_through_allowed_changes()
    {
        var rejectedStates = new List<IncidentState>();
        Incident rejected = AwaitingApproval(out ProposedPlan rejectedPlan, rejectedStates);
        Record(rejectedStates, rejected, () => rejected.Reject(rejectedPlan.PlanId, rejectedPlan.Version, "responder@example.test", "Not now.", Later));

        var revokedStates = new List<IncidentState>();
        Incident revoked = AwaitingApproval(out ProposedPlan revokedPlan, revokedStates);
        Record(revokedStates, revoked, () => revoked.Approve(revokedPlan.PlanId, revokedPlan.Version, revokedPlan.Hash, "responder@example.test", "jws", Later));
        Record(revokedStates, revoked, () => revoked.RevokeApproval(revokedPlan.PlanId, revokedPlan.Version, "responder@example.test", "Wrong target.", Later));

        rejected.State.ShouldBe(IncidentState.Escalated);
        revoked.State.ShouldBe(IncidentState.Escalated);
        rejectedStates.Concat(revokedStates).ShouldNotBeEmpty();
        rejectedStates.Zip(rejectedStates.Skip(1)).ShouldAllBe(step => IncidentStateMachine.CanMove(step.First, step.Second));
        revokedStates.Zip(revokedStates.Skip(1)).ShouldAllBe(step => IncidentStateMachine.CanMove(step.First, step.Second));
    }

    private static void Record(List<IncidentState> states, Incident incident, Func<CleanArchitecture.BuildingBlocks.Result> operation)
    {
        operation().IsSuccess.ShouldBeTrue();
        states.Add(incident.State);
    }

    private static Incident AwaitingApproval(out ProposedPlan plan, List<IncidentState> states)
    {
        LinkedChange leak = Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1");
        (Incident incident, InvestigationId investigation) = Detected(60, leak);
        states.Add(incident.State);
        var leading = HypothesisId.New();
        incident.RecordInvestigation(Report(
            investigation,
            [Supporting(leading, 0.85, 25, 60, "redis:1", "redis:2")],
            Hypothesis(leading, "v2.4.1 leaks connections.", Checkout, leak.ObservationIds[0]),
            Hypothesis(HypothesisId.New(), "Traffic grew.")), Later);
        incident.RecordDecision(FalsificationCheck.Decide(incident, DecisionId.New(), Later)).IsSuccess.ShouldBeTrue();
        states.Add(incident.State);
        plan = IncidentScenario.Plan(leading);
        incident.ProposePlan(plan).IsSuccess.ShouldBeTrue();
        states.Add(incident.State);
        return incident;
    }
}
