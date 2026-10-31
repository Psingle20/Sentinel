using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Projections;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Tests;

public sealed class IncidentTests
{
    private static readonly DateTimeOffset Later = Start.AddMinutes(5);

    [Fact]
    public void A_deployment_serving_before_onset_and_corroborated_by_two_observations_is_diagnosed()
    {
        LinkedChange leak = Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1");
        (Incident incident, InvestigationId investigation) = Detected(60, leak);
        var leading = HypothesisId.New();
        var load = HypothesisId.New();

        incident.RecordInvestigation(Report(
            investigation,
            [Supporting(leading, 0.85, 25, 60, "redis:1", "redis:2"), Supporting(leading, 0.5, 0, 20, "deploy:1")],
            Hypothesis(leading, "v2.4.1 leaks connections.", Checkout, leak.ObservationIds[0]),
            Hypothesis(load, "Traffic grew.")), Later).Value.ShouldBeTrue();

        Decision decision = FalsificationCheck.Decide(incident, DecisionId.New(), Later);

        decision.Result.ShouldBe(DecisionResult.Diagnosed);
        decision.LeadingHypothesisId.ShouldBe(leading);
        decision.RejectedHypothesisIds.ShouldBe([load]);
        decision.Checks.ShouldAllBe(check => check.Passed);
    }

    [Fact]
    public void A_deployment_still_rolling_out_when_symptoms_began_fails_precedence_and_Runtime_abstains()
    {
        // S2: the confounding deployment serves only after symptoms began.
        LinkedChange confounder = Deployment("checkout-service", "v2.4.2", "v2.4.0", 55, null, "d2");
        (Incident incident, InvestigationId investigation) = Detected(70, confounder);
        var blamed = HypothesisId.New();

        incident.RecordInvestigation(Report(
            investigation,
            [Supporting(blamed, 0.6, 55, 55, "deploy:1", "deploy:2")],
            Hypothesis(blamed, "v2.4.2 caused it.", Checkout, confounder.ObservationIds[0]),
            Hypothesis(HypothesisId.New(), "Something else.")), Later);

        Decision decision = FalsificationCheck.Decide(incident, DecisionId.New(), Later);

        decision.Result.ShouldBe(DecisionResult.Abstained);
        decision.Checks.Single(check => check.Name == FalsificationCheck.Precedence).Passed.ShouldBeFalse();
    }

    [Fact]
    public void A_cause_supported_by_a_single_observation_is_not_corroborated()
    {
        (Incident incident, InvestigationId investigation) = Detected(60);
        var surge = HypothesisId.New();
        incident.RecordInvestigation(Report(
            investigation,
            [Supporting(surge, 0.9, 0, 50, "redis:1")],
            Hypothesis(surge, "reporting-worker saturated Redis.", Reporting),
            Hypothesis(HypothesisId.New(), "Something else.")), Later);

        Decision decision = FalsificationCheck.Decide(incident, DecisionId.New(), Later);

        decision.Result.ShouldBe(DecisionResult.Abstained);
        decision.Checks.Single(check => check.Name == FalsificationCheck.Corroboration).Passed.ShouldBeFalse();
    }

    [Fact]
    public void Contradicting_evidence_lowers_a_hypothesis_below_a_better_supported_one()
    {
        LinkedChange confounder = Deployment("checkout-service", "v2.4.2", "v2.4.0", 0, 10, "d3");
        (Incident incident, InvestigationId investigation) = Detected(60, confounder);
        var deploy = HypothesisId.New();
        var surge = HypothesisId.New();
        incident.RecordInvestigation(Report(
            investigation,
            [
                Supporting(deploy, 0.6, 0, 10, "deploy:1", "deploy:2"),
                Contradicting(deploy, 0.6, "redis:checkout:1", "redis:checkout:2"),
                Supporting(surge, 0.85, 5, 55, "redis:reporting:1", "redis:reporting:2")
            ],
            Hypothesis(deploy, "v2.4.2 caused it.", Checkout, confounder.ObservationIds[0]),
            Hypothesis(surge, "reporting-worker saturated Redis.", Reporting)), Later);

        FalsificationCheck.Decide(incident, DecisionId.New(), Later).LeadingHypothesisId.ShouldBe(surge);
    }

    [Fact]
    public void Runtime_rejects_findings_that_cite_nothing_or_unknown_hypotheses_and_ignores_repeated_reports()
    {
        (Incident incident, InvestigationId investigation) = Detected(60);
        var known = HypothesisId.New();
        Evidence uncited = Supporting(known, 0.9, 0, 10) with { SourceObservationIds = [] };
        Evidence orphan = Supporting(HypothesisId.New(), 0.9, 0, 10, "x:1");
        InvestigationReport report = Report(investigation, [uncited, orphan], Hypothesis(known, "A."), Hypothesis(HypothesisId.New(), "B."));

        incident.RecordInvestigation(report, Later).Value.ShouldBeTrue();
        incident.RecordInvestigation(report, Later).Value.ShouldBeFalse();

        incident.AcceptedEvidence().ShouldBeEmpty();
        incident.Reports.Single().RejectedEvidence.Select(rejected => rejected.Reason)
            .ShouldBe(["cites no observation", "refers to an unknown hypothesis"], ignoreOrder: true);
    }

    [Fact]
    public void Approval_binds_to_the_current_plan_version_and_its_exact_hash()
    {
        Incident incident = AwaitingApproval(out ProposedPlan plan);

        incident.Approve(plan.PlanId, plan.Version, "sha256:other", "responder@example.test", "jws", Later).Error.ShouldBe(IncidentErrors.PlanHashMismatch);
        incident.Approve(PlanId.New(), plan.Version, plan.Hash, "responder@example.test", "jws", Later).Error.ShouldBe(IncidentErrors.NotCurrentPlan);
        incident.Approve(plan.PlanId, plan.Version, plan.Hash, "responder@example.test", "jws", Later).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_fencing_token_is_the_execution_request_stream_version_and_start_is_confirmed_only_for_it()
    {
        Incident incident = AwaitingApproval(out ProposedPlan plan);
        incident.Approve(plan.PlanId, plan.Version, plan.Hash, "responder@example.test", "jws", Later);

        ExecutionRequested requested = incident.RequestExecution("acme:incident:execute:1", Later).Value;

        requested.FencingToken.ShouldBe(incident.Version);
        incident.ConfirmStart(plan.PlanId, plan.Version, requested.FencingToken - 1, Later).Error.Code.ShouldBe("Incident.StaleFencingToken");
        incident.ConfirmStart(plan.PlanId, plan.Version, requested.FencingToken, Later).IsSuccess.ShouldBeTrue();
        long afterStart = incident.Version;
        incident.ConfirmStart(plan.PlanId, plan.Version, requested.FencingToken, Later).IsSuccess.ShouldBeTrue();
        incident.Version.ShouldBe(afterStart);
    }

    [Fact]
    public void A_start_is_refused_once_the_execution_has_an_outcome()
    {
        Incident incident = AwaitingApproval(out ProposedPlan plan);
        incident.Approve(plan.PlanId, plan.Version, plan.Hash, "responder@example.test", "jws", Later);
        ExecutionRequested requested = incident.RequestExecution("key", Later).Value;
        incident.RecordExecution(plan.PlanId, plan.Version, ExecutionResult.Aborted, "Runtime refused", Later);

        incident.ConfirmStart(plan.PlanId, plan.Version, requested.FencingToken, Later).IsFailure.ShouldBeTrue();
        incident.State.ShouldBe(IncidentState.Escalated);
    }

    [Fact]
    public void An_unknown_outcome_keeps_the_incident_remediating_until_the_final_outcome_arrives()
    {
        Incident incident = AwaitingApproval(out ProposedPlan plan);
        incident.Approve(plan.PlanId, plan.Version, plan.Hash, "responder@example.test", "jws", Later);
        incident.RequestExecution("key", Later);

        incident.RecordExecution(plan.PlanId, plan.Version, ExecutionResult.OutcomeUnknown, "timeout", Later);
        incident.State.ShouldBe(IncidentState.Remediating);

        incident.RecordExecution(plan.PlanId, plan.Version, ExecutionResult.ActionVerified, "reconciled", Later);
        incident.ScheduleRecovery(Later).IsSuccess.ShouldBeTrue();
        incident.State.ShouldBe(IncidentState.Verifying);
    }

    [Theory]
    [InlineData(RecoveryResult.Recovered, IncidentState.Resolved)]
    [InlineData(RecoveryResult.Unchanged, IncidentState.Escalated)]
    [InlineData(RecoveryResult.Inconclusive, IncidentState.Escalated)]
    public void Only_verified_recovery_resolves_the_incident(RecoveryResult result, IncidentState expected)
    {
        Incident incident = AwaitingApproval(out ProposedPlan plan);
        incident.Approve(plan.PlanId, plan.Version, plan.Hash, "responder@example.test", "jws", Later);
        incident.RequestExecution("key", Later);
        incident.RecordExecution(plan.PlanId, plan.Version, ExecutionResult.ActionVerified, "serving v2.4.0", Later);
        incident.ScheduleRecovery(Later);

        incident.RecordRecovery(result, [], Later, "reason", Later.AddMinutes(2));

        incident.State.ShouldBe(expected);
    }

    [Theory]
    [InlineData(RecoveryResult.Unchanged)]
    [InlineData(RecoveryResult.Degraded)]
    public void A_remedy_that_took_effect_without_recovery_opens_a_follow_up_cycle_from_the_end_of_its_window(RecoveryResult result)
    {
        Incident incident = Verifying(out ProposedPlan plan);
        DateTimeOffset observedThrough = Later.AddMinutes(1);

        incident.RecordRecovery(result, [], observedThrough, "slo_burn_rate=6.4", Later.AddMinutes(2), followUp: true).IsSuccess.ShouldBeTrue();

        incident.State.ShouldBe(IncidentState.Detected);
        incident.Cycle.ShouldBe(1);
        incident.FollowUps.ShouldBe(1);
        incident.SymptomOnset.ShouldBe(observedThrough, "the remaining symptom is known to outlast the remedy from there");
        incident.Plan.ShouldBeNull("the follow-up cycle plans afresh");
        TriedRemedy tried = incident.TriedRemedies.ShouldHaveSingleItem();
        tried.PlanId.ShouldBe(plan.PlanId);
        tried.Cycle.ShouldBe(0);
        tried.Result.ShouldBe(result);
        tried.FollowedUp.ShouldBeTrue();

        StoredEvent[] stream = [.. incident.DequeueDomainEvents().Cast<IIncidentEvent>().Select((@event, index) => new StoredEvent(index + 1, @event))];
        stream.Select(stored => stored.Event).OfType<IncidentReopened>().ShouldHaveSingleItem().FollowsPlanId.ShouldBe(plan.PlanId);
        IncidentDetail detail = IncidentProjector.Project(stream);
        detail.RemediesTried.ShouldHaveSingleItem().FollowedUp.ShouldBeTrue();
        detail.Timeline.ShouldContain(entry => entry.Summary.StartsWith("Follow-up cycle: Recovery", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(RecoveryResult.Inconclusive)]
    [InlineData(RecoveryResult.CompensationRequired)]
    public void A_verdict_that_cannot_be_trusted_or_asks_for_compensation_escalates_even_where_follow_ups_are_allowed(RecoveryResult result)
    {
        Incident incident = Verifying(out _);

        incident.RecordRecovery(result, [], Later, "reason", Later.AddMinutes(2), followUp: true);

        incident.State.ShouldBe(IncidentState.Escalated);
        incident.Cycle.ShouldBe(0);
    }

    [Fact]
    public void Only_one_follow_up_is_opened_so_a_second_unrecovered_remedy_escalates()
    {
        Incident incident = Verifying(out _);
        incident.RecordRecovery(RecoveryResult.Unchanged, [], Later, "first", Later.AddMinutes(1), followUp: true);
        var again = InvestigationId.New();
        incident.RequestInvestigation(again, "Investigate again.", Start, Later.AddMinutes(1), Later.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        var leading = HypothesisId.New();
        incident.RecordInvestigation(Report(
            again,
            [Supporting(leading, 0.85, 25, 60, "redis:1", "redis:2")],
            Hypothesis(leading, "v2.4.1 leaks connections.", Checkout, incident.Changes[0].ObservationIds[0]),
            Hypothesis(HypothesisId.New(), "Traffic grew.")), Later.AddMinutes(2));
        incident.RecordDecision(FalsificationCheck.Decide(incident, DecisionId.New(), Later.AddMinutes(2))).IsSuccess.ShouldBeTrue();
        ProposedPlan second = IncidentScenario.Plan(leading) with { Document = PlanDocument(leading) };
        incident.ProposePlan(second).IsSuccess.ShouldBeTrue();
        incident.Approve(second.PlanId, second.Version, second.Hash, "responder@example.test", "jws", Later.AddMinutes(2));
        incident.RequestExecution("key-2", Later.AddMinutes(2));
        incident.RecordExecution(second.PlanId, second.Version, ExecutionResult.ActionVerified, "serving", Later.AddMinutes(3));
        incident.ScheduleRecovery(Later.AddMinutes(3)).IsSuccess.ShouldBeTrue();

        incident.RecordRecovery(RecoveryResult.Unchanged, [], Later.AddMinutes(5), "second", Later.AddMinutes(6), followUp: true);

        incident.State.ShouldBe(IncidentState.Escalated);
        incident.Cycle.ShouldBe(1);
        incident.FollowUps.ShouldBe(1);
    }

    [Fact]
    public void Symptoms_clearing_resolves_only_an_incident_left_for_approval_or_responders()
    {
        Incident awaiting = AwaitingApproval(out _);
        awaiting.ResolveCleared("cleared", Later).IsSuccess.ShouldBeTrue();
        awaiting.State.ShouldBe(IncidentState.Resolved);

        (Incident investigating, _) = Detected(60);
        investigating.ResolveCleared("cleared", Later).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Replaying_the_stream_rebuilds_the_same_state_and_projection()
    {
        Incident incident = AwaitingApproval(out _);
        StoredEvent[] stream = [.. incident.DequeueDomainEvents().Cast<IIncidentEvent>().Select((@event, index) => new StoredEvent(index + 1, @event))];

        var replayed = Incident.Rehydrate(stream.Select(stored => stored.Event));
        IncidentDetail first = IncidentProjector.Project(stream);
        IncidentDetail second = IncidentProjector.Project(stream);

        replayed.Version.ShouldBe(incident.Version);
        replayed.State.ShouldBe(incident.State);
        second.Summary.ShouldBe(first.Summary);
        second.Timeline.ShouldBe(first.Timeline);
        first.Summary.Status.ShouldBe(IncidentStatus.AwaitingApproval);
        first.Plan!.Status.ShouldBe(PlanStatus.AwaitingApproval);
    }

    [Fact]
    public void New_events_become_the_messages_other_services_depend_on()
    {
        (Incident incident, _) = Detected(60, Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1"));
        StoredEvent[] stream = [.. incident.DequeueDomainEvents().Cast<IIncidentEvent>().Select((@event, index) => new StoredEvent(index + 1, @event))];
        var outbox = new RecordingOutbox();

        IncidentMessages.Enqueue(outbox, incident, stream, null, new InvestigationBudget(12, 40_000, TimeSpan.FromMinutes(2)), [ServiceId.From("order-service")]);

        outbox.Of<IncidentOpened>().Single().StreamVersion.ShouldBe(1);
        InvestigateIncident command = outbox.Of<InvestigateIncident>().Single();
        command.RelatedServiceIds.ShouldBe([ServiceId.From("order-service")]);
        outbox.Messages.ShouldAllBe(entry => entry.Metadata!.CorrelationId == incident.Id.ToString());
    }

    private static Incident Verifying(out ProposedPlan plan)
    {
        Incident incident = AwaitingApproval(out plan);
        incident.Approve(plan.PlanId, plan.Version, plan.Hash, "responder@example.test", "jws", Later);
        incident.RequestExecution("key", Later);
        incident.RecordExecution(plan.PlanId, plan.Version, ExecutionResult.ActionVerified, "serving v2.4.0", Later);
        incident.ScheduleRecovery(Later).IsSuccess.ShouldBeTrue();
        return incident;
    }

    private static Incident AwaitingApproval(out ProposedPlan plan)
    {
        LinkedChange leak = Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1");
        (Incident incident, InvestigationId investigation) = Detected(60, leak);
        var leading = HypothesisId.New();
        incident.RecordInvestigation(Report(
            investigation,
            [Supporting(leading, 0.85, 25, 60, "redis:1", "redis:2")],
            Hypothesis(leading, "v2.4.1 leaks connections.", Checkout, leak.ObservationIds[0]),
            Hypothesis(HypothesisId.New(), "Traffic grew.")), Later);
        incident.RecordDecision(FalsificationCheck.Decide(incident, DecisionId.New(), Later));
        plan = IncidentScenario.Plan(leading) with { Document = PlanDocument(leading) };
        incident.ProposePlan(plan).IsSuccess.ShouldBeTrue();
        return incident;
    }

    private static string PlanDocument(HypothesisId hypothesis) =>
        System.Text.Json.JsonSerializer.Serialize(
            new RemediationPlan
            {
                PlanId = PlanId.New(),
                PlanVersion = 1,
                PlanHash = "sha256:plan",
                HypothesisId = hypothesis,
                Actions = [new RollbackAction { ActionId = ActionId.New(), TargetServiceId = Checkout, FromRevision = "v2.4.1", ToRevision = "v2.4.0" }],
                ExpectedRecovery = [],
                ObservationWindow = TimeSpan.FromSeconds(90),
                Risk = RiskLevel.Medium
            },
            Sentinel.Contracts.Messaging.SentinelJson.Options);
}
