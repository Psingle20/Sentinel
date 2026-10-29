using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;

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

    private static readonly HypothesisId Lead = HypothesisId.New();
    private static readonly HypothesisId Other = HypothesisId.New();

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
