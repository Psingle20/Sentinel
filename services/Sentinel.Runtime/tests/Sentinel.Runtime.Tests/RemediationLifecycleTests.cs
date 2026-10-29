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
    public void A_plan_superseded_after_approval_can_be_neither_executed_nor_started_on_the_old_approval()
    {
        (Incident incident, ProposedPlan plan) = Approved();
        ProposedPlan revised = plan with { Version = plan.Version + 1, Hash = "sha256:revised" };

        incident.ProposePlan(revised).IsSuccess.ShouldBeTrue();

        incident.Approval.ShouldBeNull("the approval was for the version that was superseded");
        incident.RequestExecution("key", Start.AddSeconds(230)).IsFailure.ShouldBeTrue();
        incident.ConfirmStart(plan.PlanId, plan.Version, 1, Start.AddSeconds(231)).IsFailure.ShouldBeTrue();
        incident.Approve(plan.PlanId, plan.Version, plan.Hash, "responder@example.test", "jws", Start.AddSeconds(232)).IsFailure.ShouldBeTrue("an approval of the old version is refused");
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
