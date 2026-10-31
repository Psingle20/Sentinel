using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Incidents.Commands;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Domain.Incidents.Events;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;

namespace Sentinel.Runtime.Tests;

/// <summary>Phase E05: an ambiguous consensus asks the escalation investigator once per cycle before it escalates to responders.</summary>
public sealed class EscalationTests
{
    private static readonly ConsensusSettings Settings = new();
    private static readonly DateTimeOffset Now = Start.AddSeconds(300);
    private static readonly HypothesisId Lead = HypothesisId.New();
    private static readonly HypothesisId Other = HypothesisId.New();

    [Fact]
    public void An_ambiguous_consensus_on_complete_evidence_requests_an_escalation_instead_of_abstaining()
    {
        Incident incident = Ambiguous();
        EscalationSpec spec = Spec();

        Decide(incident, spec).IsSuccess.ShouldBeTrue();

        incident.Decision.ShouldBeNull();
        InvestigationRequested requested = incident.Escalation.ShouldNotBeNull();
        requested.InvestigationId.ShouldBe(spec.InvestigationId);
        requested.Role.ShouldBe(Incident.EscalationRole);
        requested.ChallengedHypothesisIds.ShouldBe([Lead, Other]);
        requested.Escalation.ShouldNotBeNull().Standing.Select(standing => standing.HypothesisId).ShouldBe([Lead, Other]);
        incident.PendingInvestigations.ShouldContain(requested);
        incident.State.ShouldBe(IncidentState.Investigating);
    }

    [Fact]
    public void While_the_escalation_is_pending_the_decision_waits_and_a_second_ambiguity_is_recorded()
    {
        Incident incident = Ambiguous();
        EscalationSpec spec = Spec();
        Decide(incident, spec).IsSuccess.ShouldBeTrue();

        Decide(incident, spec).IsSuccess.ShouldBeTrue();
        incident.Decision.ShouldBeNull();

        incident.RecordInvestigation(Report(spec.InvestigationId, []), Now.AddSeconds(30)).IsSuccess.ShouldBeTrue();
        Decide(incident, Spec()).IsSuccess.ShouldBeTrue();

        Decision decision = incident.Decision.ShouldNotBeNull();
        decision.Result.ShouldBe(DecisionResult.Abstained);
        decision.AbstainedFor.ShouldBe(AbstentionReason.Ambiguous);
        incident.Escalation.ShouldNotBeNull().InvestigationId.ShouldBe(spec.InvestigationId);
    }

    [Fact]
    public void Evidence_from_the_escalation_that_separates_the_two_lets_consensus_diagnose()
    {
        Incident incident = Ambiguous();
        EscalationSpec spec = Spec();
        Decide(incident, spec).IsSuccess.ShouldBeTrue();

        incident.RecordInvestigation(Report(spec.InvestigationId,
        [
            Supporting(Lead, 0.7, 50, 60, "escalation-1", "escalation-2") with { IndependenceGroup = "escalation:redis_connections" },
            Contradicting(Other, 0.7, "escalation-1", "escalation-2") with { IndependenceGroup = "escalation:redis_connections" }
        ]), Now.AddSeconds(30)).IsSuccess.ShouldBeTrue();
        Decide(incident, Spec()).IsSuccess.ShouldBeTrue();

        Decision decision = incident.Decision.ShouldNotBeNull();
        decision.Result.ShouldBe(DecisionResult.Diagnosed);
        decision.LeadingHypothesisId.ShouldBe(Lead);
    }

    [Fact]
    public void Without_an_escalation_or_on_partial_evidence_the_ambiguity_is_recorded_at_once()
    {
        Incident disabled = Ambiguous();
        Incident partial = Ambiguous();

        Decide(disabled, null).IsSuccess.ShouldBeTrue();
        Decide(partial, Spec() with { MinimumCompleteness = 1.01 }).IsSuccess.ShouldBeTrue();

        disabled.Decision.ShouldNotBeNull().AbstainedFor.ShouldBe(AbstentionReason.Ambiguous);
        partial.Decision.ShouldNotBeNull().AbstainedFor.ShouldBe(AbstentionReason.Ambiguous);
        disabled.Escalation.ShouldBeNull();
        partial.Escalation.ShouldBeNull();
    }

    [Fact]
    public void An_abstention_for_thin_evidence_never_escalates()
    {
        (Incident incident, InvestigationId investigation) = Detected(60, Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1"));
        incident.RecordInvestigation(Report(investigation,
            [Supporting(Lead, 0.95, 0, 50, "surge-1") with { IndependenceGroup = "redis-connections:checkout-service" }],
            Hypothesis(Lead, "The leak.", Checkout, ObservationId.From("deploy-controller:deployment:d1:started"))), Start.AddSeconds(200)).IsSuccess.ShouldBeTrue();

        Decide(incident, Spec()).IsSuccess.ShouldBeTrue();

        incident.Decision.ShouldNotBeNull().AbstainedFor.ShouldBe(AbstentionReason.InsufficientEvidence);
        incident.Escalation.ShouldBeNull();
    }

    [Fact]
    public void Requesting_the_same_escalation_twice_appends_nothing_and_a_rehydrated_incident_still_waits()
    {
        Incident incident = Ambiguous();
        EscalationSpec spec = Spec();
        Decision ambiguous = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now);

        incident.RequestEscalation(spec.InvestigationId, spec.Objective, spec.WindowFrom, spec.WindowTo, Now, ambiguous).IsSuccess.ShouldBeTrue();
        incident.RequestEscalation(spec.InvestigationId, spec.Objective, spec.WindowFrom, spec.WindowTo, Now, ambiguous).IsSuccess.ShouldBeTrue();
        incident.RequestEscalation(InvestigationId.New(), spec.Objective, spec.WindowFrom, spec.WindowTo, Now, ambiguous).IsFailure.ShouldBeTrue();

        IIncidentEvent[] events = [.. incident.DequeueDomainEvents().Cast<IIncidentEvent>()];
        events.OfType<InvestigationRequested>().Count(requested => requested.Role == Incident.EscalationRole).ShouldBe(1);
        var rehydrated = Incident.Rehydrate(events);
        Decide(rehydrated, Spec()).IsSuccess.ShouldBeTrue();
        rehydrated.Decision.ShouldBeNull();
        rehydrated.Escalation.ShouldNotBeNull().Escalation.ShouldNotBeNull().EvidenceCompleteness.ShouldBe(ambiguous.EvidenceCompleteness);
    }

    /// <summary>Two explanations, each with two independent evidence families, too close for consensus to decide between.</summary>
    private static Incident Ambiguous()
    {
        (Incident incident, InvestigationId investigation) = Detected(60, Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1"));
        Evidence[] evidence =
        [
            Supporting(Lead, 0.85, 20, 50, "surge-1", "surge-2") with { IndependenceGroup = "redis-connections:checkout-service" },
            Supporting(Lead, 0.5, 0, 20, "deploy-1", "deploy-2") with { IndependenceGroup = "deployment:d1" },
            Supporting(Other, 0.8, 0, 50, "b-1") with { IndependenceGroup = "redis-connections:reporting-worker" },
            Supporting(Other, 0.5, 0, 50, "b-2") with { IndependenceGroup = "deployment:other" }
        ];
        incident.RecordInvestigation(Report(investigation, evidence,
            Hypothesis(Lead, "The leak.", Checkout, ObservationId.From("deploy-controller:deployment:d1:started")),
            Hypothesis(Other, "The batch.", Reporting)), Start.AddSeconds(200)).IsSuccess.ShouldBeTrue();
        return incident;
    }

    private static EscalationSpec Spec() => new(InvestigationId.New(), "Separate the two explanations.", Start.AddSeconds(-840), Now, 0.8);

    private static Result<Incident> Decide(Incident incident, EscalationSpec? spec) =>
        IncidentCommandProcessor.Apply(incident.Id, incident, new DecideCommand(DecisionId.New(), UseConsensus: true, Settings, spec), Now);
}
