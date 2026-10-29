using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;
using Sentinel.Runtime.Domain.Correlation.Decisions;

namespace Sentinel.Runtime.Tests;

public sealed class IncidentLifecycleTests
{
    [Fact]
    public void Repeating_an_investigation_request_appends_nothing()
    {
        (Incident incident, InvestigationId investigation) = Detected(60);
        incident.DequeueDomainEvents();

        incident.RequestInvestigation(investigation, "Find the cause.", Start, Start.AddSeconds(70), Start.AddSeconds(80)).IsSuccess.ShouldBeTrue();

        incident.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void An_investigation_that_does_not_report_is_timed_out_and_no_longer_pending()
    {
        (Incident incident, InvestigationId investigation) = Detected(60);

        incident.TimeOutInvestigation(investigation, Start.AddSeconds(300)).IsSuccess.ShouldBeTrue();

        incident.PendingInvestigations.ShouldBeEmpty();
        incident.TimedOutInvestigationIds.ShouldContain(investigation);
        long version = incident.Version;
        incident.TimeOutInvestigation(investigation, Start.AddSeconds(301));
        incident.Version.ShouldBe(version);
    }

    [Fact]
    public void A_later_decision_supersedes_the_first_and_keeps_it_in_the_history()
    {
        (Incident incident, InvestigationId investigation) = Detected(60);
        var cause = HypothesisId.New();
        incident.RecordInvestigation(Report(investigation, [Supporting(cause, 0.8, 0, 50, "a", "b")], Hypothesis(cause, "A cause.")), Start.AddSeconds(200));
        Decision first = FalsificationCheck.Decide(incident, DecisionId.New(), Start.AddSeconds(210));
        incident.RecordDecision(first);
        incident.ProposePlan(Plan(cause)).IsSuccess.ShouldBeTrue();

        Decision second = first with { DecisionId = DecisionId.New(), Result = DecisionResult.Abstained, LeadingHypothesisId = null, DecidedAt = Start.AddSeconds(240) };
        incident.SupersedeDecision(second, "A late observation contradicts the leader.").IsSuccess.ShouldBeTrue();

        incident.Decision.ShouldBe(second);
        incident.Plan.ShouldBeNull();
        incident.Decisions.Count.ShouldBe(2);
        incident.Decisions[0].SupersededBy.ShouldBe(second.DecisionId);
        incident.State.ShouldBe(IncidentState.Escalated);
    }

    [Fact]
    public void A_decision_cannot_be_superseded_once_execution_was_requested()
    {
        (Incident incident, InvestigationId investigation) = Detected(60);
        var cause = HypothesisId.New();
        incident.RecordInvestigation(Report(investigation, [Supporting(cause, 0.8, 0, 50, "a", "b")], Hypothesis(cause, "A cause.")), Start.AddSeconds(200));
        Decision first = FalsificationCheck.Decide(incident, DecisionId.New(), Start.AddSeconds(210));
        incident.RecordDecision(first);
        ProposedPlan plan = Plan(cause);
        incident.ProposePlan(plan);
        incident.Approve(plan.PlanId, 1, plan.Hash, "responder@example.test", "jws", Start.AddSeconds(220));
        incident.RequestExecution("key", Start.AddSeconds(221));

        incident.SupersedeDecision(first with { DecisionId = DecisionId.New() }, "too late").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_reopened_incident_starts_a_new_cycle_and_keeps_its_history()
    {
        (Incident incident, _) = Detected(60);
        incident.Escalate("handed over", Start.AddSeconds(100));
        incident.ResolveCleared("cleared", Start.AddSeconds(200)).IsSuccess.ShouldBeTrue();
        long resolvedAt = incident.Version;

        incident.Reopen("It came back.", [ObservationId.From("prometheus:slo_burn_rate:checkout-service:9")], Start.AddSeconds(400), Start.AddSeconds(410)).IsSuccess.ShouldBeTrue();

        incident.State.ShouldBe(IncidentState.Detected);
        incident.Cycle.ShouldBe(1);
        incident.SymptomOnset.ShouldBe(Start.AddSeconds(400));
        incident.Decision.ShouldBeNull();
        incident.Version.ShouldBe(resolvedAt + 1);
        incident.Reopen("again", [], Start, Start).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_merged_signal_widens_the_blast_radius_once()
    {
        (Incident incident, _) = Detected(60);
        var orders = ServiceId.From("order-service");

        incident.LinkSignal(orders, "latency_p95", 1.2, [ObservationId.From("prometheus:latency_p95:order-service:1")], 0.6, ["neighbour"], Start.AddSeconds(90));
        incident.LinkSignal(orders, "latency_p95", 1.4, [ObservationId.From("prometheus:latency_p95:order-service:2")], 0.6, ["neighbour"], Start.AddSeconds(95));

        incident.AffectedServices.ShouldBe([Checkout, orders]);
        incident.LinkedSignals.ShouldHaveSingleItem();
    }
}
