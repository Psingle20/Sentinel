using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Domain.Incidents;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;

namespace Sentinel.Runtime.Tests;

public sealed class BlackboardTests
{
    private const string Leak = "change:deploy-controller:deployment:d1:started";
    private const string Saturation = "redis-saturation";
    private const string BatchSaturation = "redis-saturation:reporting-worker";

    [Fact]
    public void Specialists_proposing_the_same_key_merge_into_one_hypothesis_with_their_lineage()
    {
        (Incident incident, InvestigationId metrics, InvestigationId changes, _) = WithSpecialists();
        var fromMetrics = HypothesisId.New();
        var fromChanges = HypothesisId.New();

        incident.RecordInvestigation(Report(metrics, [Supporting(fromMetrics, 0.85, 20, 50, "surge-1", "surge-2") with { IndependenceGroup = "redis-connections:checkout-service" }], Keyed(fromMetrics, Leak)), Start.AddSeconds(200));
        incident.RecordInvestigation(Report(changes, [Supporting(fromChanges, 0.5, 0, 20, "deploy-1", "deploy-2") with { IndependenceGroup = "deployment:d1" }], Keyed(fromChanges, Leak)), Start.AddSeconds(201));

        Hypothesis merged = incident.AllHypotheses().ShouldHaveSingleItem();
        merged.HypothesisId.ShouldBe(fromMetrics);
        incident.AcceptedEvidence().ShouldAllBe(evidence => evidence.HypothesisIds.Single() == fromMetrics);
        BlackboardHypothesis entry = incident.Blackboard.All.ShouldHaveSingleItem();
        entry.Lineage.ShouldBe([fromMetrics, fromChanges]);
        entry.ProposedBy.ShouldBe([metrics, changes]);
    }

    [Fact]
    public void A_refinement_replaces_the_broader_explanation_and_inherits_its_evidence()
    {
        (Incident incident, InvestigationId metrics, _, InvestigationId logs) = WithSpecialists();
        var culprit = HypothesisId.New();
        var broad = HypothesisId.New();
        Evidence logEvidence = Supporting(broad, 0.6, 40, 60, "log-1") with { IndependenceGroup = "logs:checkout-service:redis-maxclients" };

        incident.RecordInvestigation(Report(metrics, [Supporting(culprit, 0.85, 0, 40, "surge-1", "surge-2") with { IndependenceGroup = "redis-connections:reporting-worker" }], Keyed(culprit, BatchSaturation)), Start.AddSeconds(200));
        incident.RecordInvestigation(Report(logs, [logEvidence], Keyed(broad, Saturation)), Start.AddSeconds(201));

        incident.AllHypotheses().ShouldHaveSingleItem().HypothesisId.ShouldBe(culprit);
        incident.AcceptedEvidence().Single(evidence => evidence.EvidenceId == logEvidence.EvidenceId).HypothesisIds.ShouldBe([culprit]);
        BlackboardHypothesis parent = incident.Blackboard.All.Single(entry => entry.Hypothesis.Key == Saturation);
        parent.Subsumed.ShouldBeTrue();
        incident.Blackboard.All.Single(entry => entry.Hypothesis.Key == BatchSaturation).Refines.ShouldBe(broad);
    }

    [Fact]
    public void Without_a_refinement_the_broader_explanation_competes_on_its_own()
    {
        (Incident incident, _, _, InvestigationId logs) = WithSpecialists();
        var broad = HypothesisId.New();

        incident.RecordInvestigation(Report(logs, [Supporting(broad, 0.6, 40, 60, "log-1")], Keyed(broad, Saturation)), Start.AddSeconds(201));

        incident.AllHypotheses().ShouldHaveSingleItem().Key.ShouldBe(Saturation);
    }

    [Fact]
    public void The_projection_shows_lineage_refinement_and_a_causal_graph()
    {
        (Incident incident, InvestigationId metrics, InvestigationId changes, InvestigationId logs) = WithSpecialists(Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1"));
        var leak = HypothesisId.New();
        var sameLeak = HypothesisId.New();
        var broad = HypothesisId.New();
        incident.RecordInvestigation(Report(metrics, [Supporting(leak, 0.85, 20, 50, "surge-1", "surge-2")], Keyed(leak, Leak, ObservationId.From("deploy-controller:deployment:d1:started"))), Start.AddSeconds(200));
        incident.RecordInvestigation(Report(changes, [Supporting(sameLeak, 0.5, 0, 20, "deploy-controller:deployment:d1:started")], Keyed(sameLeak, Leak)), Start.AddSeconds(201));
        incident.RecordInvestigation(Report(logs, [Supporting(broad, 0.6, 40, 60, "log-1")], Keyed(broad, Saturation)), Start.AddSeconds(202));

        IncidentDetail detail = IncidentProjector.Project([.. incident.DequeueDomainEvents().Cast<IIncidentEvent>().Select((@event, index) => new StoredEvent(index + 1, @event))]);

        detail.Hypotheses.Count.ShouldBe(2);
        detail.Hypotheses.Single(view => view.Finding.Key == Leak).Lineage.ShouldNotBeNull().Count.ShouldBe(2);
        CausalGraphView graph = detail.CausalGraph.ShouldNotBeNull();
        graph.Nodes.Count(node => node.Kind == CausalNodeKind.Evidence).ShouldBe(3);
        graph.Edges.ShouldContain(edge => edge.Relation == "blames" && edge.To == "change:d1");
        graph.Edges.ShouldContain(edge => edge.Relation == "affects" && edge.To == "service:checkout-service");
        graph.Edges.Count(edge => edge.Relation == "supports").ShouldBe(3);
    }

    private static (Incident Incident, InvestigationId Metrics, InvestigationId Changes, InvestigationId Logs) WithSpecialists(params LinkedChange[] changes)
    {
        (Incident incident, InvestigationId generalist) = Detected(60, changes);
        var metrics = InvestigationId.New();
        var changeIds = InvestigationId.New();
        var logs = InvestigationId.New();
        incident.RequestInvestigation(metrics, "metrics", Start, Start.AddSeconds(70), Start.AddSeconds(70), "metrics");
        incident.RequestInvestigation(changeIds, "changes", Start, Start.AddSeconds(70), Start.AddSeconds(70), "changes");
        incident.RequestInvestigation(logs, "logs", Start, Start.AddSeconds(70), Start.AddSeconds(70), "logs");
        incident.TimeOutInvestigation(generalist, Start.AddSeconds(71));
        return (incident, metrics, changeIds, logs);
    }

    private static Hypothesis Keyed(HypothesisId id, string key, ObservationId? change = null) =>
        Hypothesis(id, $"Explanation {key}.", change: change) with { Key = key };
}
