using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Tests;

/// <summary>Phase 05 required behaviours of reliability-weighted consensus.</summary>
public sealed class ConsensusTests
{
    private static readonly ConsensusSettings Settings = new();
    private static readonly DateTimeOffset Now = Start.AddSeconds(300);

    [Fact]
    public void One_uncorroborated_statement_never_yields_a_diagnosis()
    {
        (Incident incident, HypothesisId cause, _) = Recorded(Supporting(Lead, 0.95, 0, 50, "surge-1", "surge-2") with { IndependenceGroup = "redis-connections:checkout-service" });

        Decision decision = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now);

        decision.Result.ShouldBe(DecisionResult.Abstained);
        decision.Checks.Single(check => check.Name == "independent-corroboration").Passed.ShouldBeFalse();
        cause.ShouldBe(Lead);
    }

    [Fact]
    public void Copies_of_one_signal_count_as_one_family_however_they_are_labelled()
    {
        (Incident incident, _, _) = Recorded(
            Supporting(Lead, 0.85, 0, 50, "surge-1", "surge-2") with { IndependenceGroup = "redis-connections:checkout-service" },
            Supporting(Lead, 0.85, 0, 50, "surge-2", "surge-3") with { IndependenceGroup = "verifier:checkout-service" });

        Decision decision = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now);

        decision.Assessments.Single(assessment => assessment.HypothesisId == Lead).IndependentSupport.ShouldBe(1);
        decision.Result.ShouldBe(DecisionResult.Abstained);
    }

    [Fact]
    public void Two_independent_families_diagnose_and_the_decision_names_the_loser()
    {
        (Incident incident, _, HypothesisId other) = Recorded(Corroborated());

        Decision decision = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now);

        decision.Result.ShouldBe(DecisionResult.Diagnosed);
        decision.LeadingHypothesisId.ShouldBe(Lead);
        decision.Confidence.ShouldBeGreaterThanOrEqualTo(Settings.AbstainBelowConfidence);
        decision.EvidenceCompleteness.ShouldBe(1);
        decision.RejectedHypothesisIds.ShouldContain(other);
        decision.AlgorithmVersion.ShouldBe(ConsensusSettings.AlgorithmVersion);
    }

    [Fact]
    public void Contradicting_evidence_lowers_the_score_and_confidence()
    {
        (Incident clean, _, _) = Recorded(Corroborated());
        (Incident contradicted, _, _) = Recorded([.. Corroborated(), Contradicting(Lead, 0.6, "flat-1", "flat-2") with { IndependenceGroup = "latency:checkout-service" }]);

        Decision first = Consensus.Decide(clean, [], Settings, DecisionId.New(), Now);
        Decision second = Consensus.Decide(contradicted, [], Settings, DecisionId.New(), Now);

        second.Assessments.Single(assessment => assessment.HypothesisId == Lead).Score.ShouldBeLessThan(first.Assessments.Single(assessment => assessment.HypothesisId == Lead).Score);
        second.Confidence.ShouldBeLessThan(first.Confidence);
    }

    [Fact]
    public void A_falsified_hypothesis_cannot_lead_however_strong_its_support()
    {
        (Incident incident, _, HypothesisId other) = Recorded(Corroborated());
        Challenge falsified = new(Lead, "change-serving-before-onset", ChallengeVerdict.Falsified, [], "Not serving before onset.");

        Decision decision = Consensus.Decide(incident, [falsified], Settings, DecisionId.New(), Now);

        decision.LeadingHypothesisId.ShouldNotBe(Lead);
        decision.Assessments.Single(assessment => assessment.HypothesisId == Lead).Falsified.ShouldBeTrue();
        decision.Result.ShouldBe(DecisionResult.Abstained);
        other.ShouldNotBe(Lead);
    }

    [Fact]
    public void A_close_alternative_is_kept_as_dissent_and_lowers_confidence()
    {
        (Incident incident, _, HypothesisId other) = Recorded(
        [
            .. Corroborated(),
            Supporting(Other, 0.8, 0, 50, "b-1") with { IndependenceGroup = "redis-connections:reporting-worker" },
            Supporting(Other, 0.5, 0, 50, "b-2") with { IndependenceGroup = "deployment:other" }
        ]);

        Decision decision = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now);

        decision.DissentingHypothesisIds.ShouldContain(other);
        decision.Confidence.ShouldBeLessThan(0.5);
        decision.Result.ShouldBe(DecisionResult.Abstained);
    }

    [Fact]
    public void Evidence_from_partial_data_lowers_completeness_and_with_it_confidence()
    {
        (Incident complete, _, _) = Recorded(Corroborated());
        (Incident partial, _, _) = Recorded([.. Corroborated().Select(evidence => evidence with { Partial = true })]);

        Consensus.Decide(partial, [], Settings, DecisionId.New(), Now).EvidenceCompleteness
            .ShouldBeLessThan(Consensus.Decide(complete, [], Settings, DecisionId.New(), Now).EvidenceCompleteness);
    }

    [Fact]
    public void Replaying_the_same_versioned_inputs_gives_the_same_decision()
    {
        (Incident incident, _, _) = Recorded(Corroborated());
        Challenge survived = new(Lead, "mechanism-follows-change", ChallengeVerdict.Survived, [], "Shifted after the change.");

        Decision first = Consensus.Decide(incident, [survived], Settings, DecisionId.New(), Now);
        Decision replay = Consensus.Decide(Incident.Rehydrate(Events(incident)), [survived], Settings, DecisionId.New(), Now.AddDays(1));

        replay.Result.ShouldBe(first.Result);
        replay.LeadingHypothesisId.ShouldBe(first.LeadingHypothesisId);
        replay.Confidence.ShouldBe(first.Confidence);
        replay.InputsHash.ShouldBe(first.InputsHash);
        Consensus.Decide(incident, [], Settings, DecisionId.New(), Now).InputsHash.ShouldNotBe(first.InputsHash);
    }

    [Fact]
    public void Below_the_configured_threshold_consensus_abstains()
    {
        (Incident incident, _, _) = Recorded(Corroborated());

        Consensus.Decide(incident, [], Settings with { AbstainBelowConfidence = 0.99 }, DecisionId.New(), Now).Result.ShouldBe(DecisionResult.Abstained);
    }

    [Fact]
    public void Redis_saturation_caused_by_the_leading_change_agrees_with_it_instead_of_competing()
    {
        var saturation = HypothesisId.New();
        Incident incident = WithSaturation(saturation, "redis-saturation");

        Decision current = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now);
        Decision v1 = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now, ConsensusSettings.AlgorithmV1);

        current.Result.ShouldBe(DecisionResult.Diagnosed);
        current.LeadingHypothesisId.ShouldBe(Lead);
        current.RejectedHypothesisIds.ShouldNotContain(saturation);
        current.DissentingHypothesisIds.ShouldNotContain(saturation);
        v1.Result.ShouldBe(DecisionResult.Abstained, "v1 counted the leak's own mechanism as the runner-up");
        v1.AlgorithmVersion.ShouldBe(ConsensusSettings.AlgorithmV1);
        v1.InputsHash.ShouldNotBe(current.InputsHash);
    }

    [Fact]
    public void Redis_saturation_blamed_on_another_service_still_competes_with_the_change()
    {
        var saturation = HypothesisId.New();

        Decision decision = Consensus.Decide(WithSaturation(saturation, "redis-saturation:reporting-worker"), [], Settings, DecisionId.New(), Now);

        decision.Result.ShouldBe(DecisionResult.Abstained);
    }

    private static readonly HypothesisId Lead = HypothesisId.New();
    private static readonly HypothesisId Other = HypothesisId.New();

    /// <summary>The leak with its change and a saturation hypothesis that shares only the connection-surge evidence.</summary>
    private static Incident WithSaturation(HypothesisId saturation, string key)
    {
        (Incident incident, InvestigationId investigation) = Detected(60, Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1"));
        Evidence[] evidence =
        [
            .. Corroborated(),
            Supporting(saturation, 0.85, 20, 50, "surge-1", "surge-2") with { IndependenceGroup = "redis-connections:checkout-service" }
        ];
        incident.RecordInvestigation(Report(investigation, evidence,
            Hypothesis(Lead, "The v2.4.1 deployment leaks connections.", Checkout, ObservationId.From("deploy-controller:deployment:d1:started")) with { Key = "change:deploy-controller:deployment:d1:started" },
            Hypothesis(saturation, "Redis ran out of client connections.") with { Key = key }), Start.AddSeconds(200)).IsSuccess.ShouldBeTrue();
        return incident;
    }

    private static Evidence[] Corroborated() =>
    [
        Supporting(Lead, 0.85, 20, 50, "surge-1", "surge-2") with { IndependenceGroup = "redis-connections:checkout-service" },
        Supporting(Lead, 0.5, 0, 20, "deploy-1", "deploy-2") with { IndependenceGroup = "deployment:d1" }
    ];

    private static (Incident Incident, HypothesisId Lead, HypothesisId Other) Recorded(params Evidence[] evidence)
    {
        (Incident incident, InvestigationId investigation) = Detected(60, Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1"));
        incident.RecordInvestigation(Report(investigation, evidence,
            Hypothesis(Lead, "The leak.", Checkout, ObservationId.From("deploy-controller:deployment:d1:started")),
            Hypothesis(Other, "The batch.", Reporting)), Start.AddSeconds(200)).IsSuccess.ShouldBeTrue();
        return (incident, Lead, Other);
    }

    private static IIncidentEvent[] Events(Incident incident) => [.. incident.DequeueDomainEvents().Cast<IIncidentEvent>()];
}
