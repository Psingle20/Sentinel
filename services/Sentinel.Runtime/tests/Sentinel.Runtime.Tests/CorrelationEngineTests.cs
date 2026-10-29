using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;
using Sentinel.Runtime.Domain.Correlation.Decisions;

namespace Sentinel.Runtime.Tests;

public sealed class CorrelationEngineTests
{
    private static readonly ServiceId Orders = ServiceId.From("order-service");
    private static readonly ServiceId Inventory = ServiceId.From("inventory-service");
    private static readonly ServiceId Billing = ServiceId.From("billing-service");
    private static readonly ServiceTopology Topology = new([(Checkout, Orders), (Billing, Inventory)]);
    private static readonly CorrelationSettings Settings = new();

    [Fact]
    public void A_signal_with_no_related_incident_opens_one_and_links_scored_changes_with_reasons()
    {
        LinkedChange recent = Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d-recent");
        LinkedChange dependency = Deployment("order-service", "v1.8.0", "v1.7.0", -60, -40, "d-orders");
        LinkedChange stale = Deployment("order-service", "v1.7.9", "v1.7.8", -1500, -1480, "d-stale");
        LinkedChange unrelated = Deployment("inventory-service", "v3.0.0", "v2.9.0", 10, 30, "d-inventory");

        CorrelationDecision decision = CorrelationEngine.Correlate(Signal(Checkout, 90), [], [recent, dependency, stale, unrelated], Topology, [], Settings);

        OpenIncident open = decision.ShouldBeOfType<OpenIncident>();
        open.Changes.Select(change => change.DeploymentId).ShouldBe(["d-recent", "d-orders"]);
        open.Changes[0].CorrelationScore!.Value.ShouldBeGreaterThan(open.Changes[1].CorrelationScore!.Value);
        open.Changes[0].CorrelationReasons.ShouldContain("same service as the symptom (checkout-service)");
        open.Changes[1].CorrelationReasons.ShouldContain("direct dependency neighbour of checkout-service");
        open.Reasons.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_change_still_rolling_out_at_onset_is_linked_so_investigators_can_rule_it_out()
    {
        LinkedChange rolling = Deployment("checkout-service", "v2.4.2", "v2.4.1", 60, 105, "d-rolling");

        OpenIncident open = CorrelationEngine.Correlate(Signal(Checkout, 90), [], [rolling], Topology, [], Settings).ShouldBeOfType<OpenIncident>();

        open.Changes.ShouldHaveSingleItem().CorrelationReasons.ShouldContain(reason => reason.StartsWith("not serving until", StringComparison.Ordinal));
    }

    [Fact]
    public void A_related_services_symptom_near_an_open_incident_merges_into_it()
    {
        IncidentContext open = Open(Checkout, 60);

        MergeIntoIncident merge = CorrelationEngine.Correlate(Signal(Orders, 120), [open], [], Topology, [], Settings).ShouldBeOfType<MergeIntoIncident>();

        merge.IncidentId.ShouldBe(open.IncidentId);
        merge.Score.ShouldBeInRange(0.01, 1);
        merge.Reasons.ShouldContain(reason => reason.Contains("dependency neighbour", StringComparison.Ordinal));
    }

    [Fact]
    public void Unrelated_simultaneous_failures_stay_separate_incidents()
    {
        IncidentContext checkout = Open(Checkout, 60);

        CorrelationEngine.Correlate(Signal(Inventory, 65), [checkout], [], Topology, [], Settings).ShouldBeOfType<OpenIncident>();
    }

    [Fact]
    public void A_related_symptom_long_after_the_onset_is_its_own_incident()
    {
        IncidentContext checkout = Open(Checkout, 0);

        CorrelationEngine.Correlate(Signal(Orders, 3600), [checkout], [], Topology, [], Settings).ShouldBeOfType<OpenIncident>();
    }

    [Fact]
    public void Further_samples_of_an_open_incident_are_absorbed()
    {
        IncidentContext open = Open(Checkout, 60);

        CorrelationEngine.Correlate(Signal(Checkout, 70), [open], [], Topology, [], Settings).ShouldBeOfType<AbsorbIntoIncident>().IncidentId.ShouldBe(open.IncidentId);
    }

    [Fact]
    public void The_same_symptom_soon_after_resolution_with_no_new_change_reopens()
    {
        IncidentContext resolved = Open(Checkout, 0) with { IsOpen = false, ResolvedAt = Start.AddSeconds(300) };

        CorrelationEngine.Correlate(Signal(Checkout, 420), [resolved], [], Topology, [], Settings).ShouldBeOfType<ReopenIncident>().IncidentId.ShouldBe(resolved.IncidentId);
    }

    [Fact]
    public void A_recurrence_with_a_new_change_is_a_new_incident()
    {
        IncidentContext resolved = Open(Checkout, 0) with { IsOpen = false, ResolvedAt = Start.AddSeconds(300) };
        LinkedChange newer = Deployment("checkout-service", "v2.4.2", "v2.4.1", 360, 380, "d-new");

        CorrelationEngine.Correlate(Signal(Checkout, 420), [resolved], [newer], Topology, [], Settings).ShouldBeOfType<OpenIncident>();
    }

    [Fact]
    public void A_maintenance_window_suppresses_its_signals()
    {
        var window = new Suppression(Checkout, null, Start, Start.AddHours(1), "planned failover");

        CorrelationEngine.Correlate(Signal(Checkout, 90), [], [], Topology, [window], Settings)
            .ShouldBeOfType<SuppressSignal>().Reasons.ShouldContain(reason => reason.Contains("planned failover", StringComparison.Ordinal));
        CorrelationEngine.Correlate(Signal(Orders, 90), [], [], Topology, [window], Settings).ShouldBeOfType<OpenIncident>();
    }

    [Fact]
    public void Topology_distance_is_undirected_and_bounded()
    {
        var chain = new ServiceTopology([(Checkout, Orders), (Orders, Inventory)]);

        chain.Distance(Inventory, Checkout).ShouldBe(2);
        chain.Distance(Checkout, Billing).ShouldBeNull();
        chain.Distance(Checkout, Inventory, maximum: 1).ShouldBeNull();
    }

    [Theory]
    [InlineData(20.0, 1, IncidentSeverity.Critical)]
    [InlineData(8.0, 1, IncidentSeverity.High)]
    [InlineData(3.0, 1, IncidentSeverity.Medium)]
    [InlineData(3.0, 3, IncidentSeverity.High)]
    [InlineData(1.5, 1, IncidentSeverity.Low)]
    public void Severity_follows_burn_rate_and_blast_radius(double burnRate, int affected, IncidentSeverity expected)
    {
        SeverityPolicy.Assess(burnRate, affected).ShouldBe(expected);
    }

    private static CorrelationSignal Signal(ServiceId service, int onsetAt) =>
        new(service, "slo_burn_rate", 12, Start.AddSeconds(onsetAt), Start.AddSeconds(onsetAt + 10), ObservationId.From($"prometheus:slo_burn_rate:{service}:{onsetAt}"));

    private static IncidentContext Open(ServiceId service, int onsetAt) =>
        new(IncidentId.New(), service, [service], Start.AddSeconds(onsetAt), true, null);
}

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
