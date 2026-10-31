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
    public void An_abstention_names_its_reason_and_only_ambiguity_names_the_two_contenders()
    {
        (Incident close, HypothesisId lead, HypothesisId other) = Recorded(
        [
            .. Corroborated(),
            Supporting(Other, 0.8, 0, 50, "b-1") with { IndependenceGroup = "redis-connections:reporting-worker" },
            Supporting(Other, 0.5, 0, 50, "b-2") with { IndependenceGroup = "deployment:other" }
        ]);
        (Incident thin, _, _) = Recorded(Supporting(Lead, 0.95, 0, 50, "surge-1") with { IndependenceGroup = "redis-connections:checkout-service" });
        (Incident refuted, _, _) = Recorded(Corroborated());
        Challenge[] both =
        [
            new(Lead, "change-served-before-onset", ChallengeVerdict.Falsified, [], "Served after onset."),
            new(Other, "load-rose-before-onset", ChallengeVerdict.Falsified, [], "No load rose.")
        ];

        Decision ambiguous = Consensus.Decide(close, [], Settings, DecisionId.New(), Now);
        Decision insufficient = Consensus.Decide(thin, [], Settings, DecisionId.New(), Now);
        Decision lowered = Consensus.Decide(refuted, [], Settings with { AbstainBelowConfidence = 0.99 }, DecisionId.New(), Now);

        ambiguous.AbstainedFor.ShouldBe(AbstentionReason.Ambiguous);
        ambiguous.Contenders.ShouldBe([lead, other]);
        insufficient.AbstainedFor.ShouldBe(AbstentionReason.InsufficientEvidence);
        insufficient.Contenders.ShouldBeEmpty();
        lowered.AbstainedFor.ShouldBe(AbstentionReason.InsufficientEvidence);
        Consensus.Decide(refuted, both, Settings, DecisionId.New(), Now).AbstainedFor.ShouldBe(AbstentionReason.Falsified);
        Consensus.Decide(refuted, [], Settings, DecisionId.New(), Now).AbstainedFor.ShouldBeNull();
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

    [Fact]
    public void A_standing_change_claims_the_saturation_blamed_on_its_service_even_when_the_saturation_scored_higher()
    {
        var saturation = HypothesisId.New();
        Incident incident = WithStrongSaturation(saturation, "redis-saturation:checkout-service");

        Decision current = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now);
        Decision v3 = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now, ConsensusSettings.AlgorithmV3);
        Decision v2 = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now, ConsensusSettings.AlgorithmV2);

        current.AlgorithmVersion.ShouldBe(ConsensusSettings.AlgorithmVersion);
        v3.LeadingHypothesisId.ShouldBe(Lead, "v3 introduced the claim");
        current.Result.ShouldBe(DecisionResult.Diagnosed);
        current.LeadingHypothesisId.ShouldBe(Lead);
        current.RejectedHypothesisIds.ShouldNotContain(saturation);
        current.DissentingHypothesisIds.ShouldNotContain(saturation);
        HypothesisAssessment change = current.Assessments.Single(assessment => assessment.HypothesisId == Lead);
        change.IndependentSupport.ShouldBe(4, "the deployment and the saturation's three signals, each family once");
        change.Reason.ShouldBe("Stands, and explains redis-saturation:checkout-service.");
        v2.LeadingHypothesisId.ShouldBe(saturation, "v2 let the mechanism outscore the change that caused it, and replays that way");
    }

    [Theory]
    [InlineData("redis-saturation")]
    [InlineData("redis-saturation:reporting-worker")]
    public void Saturation_with_no_culprit_or_another_culprit_is_not_claimed(string key)
    {
        var saturation = HypothesisId.New();

        Decision decision = Consensus.Decide(WithStrongSaturation(saturation, key), [], Settings, DecisionId.New(), Now);

        decision.LeadingHypothesisId.ShouldBe(saturation);
        decision.Assessments.Single(assessment => assessment.HypothesisId == Lead).IndependentSupport.ShouldBe(1);
    }

    [Fact]
    public void Two_standing_changes_to_the_service_leave_its_saturation_unclaimed()
    {
        var saturation = HypothesisId.New();

        Decision decision = Consensus.Decide(WithStrongSaturation(saturation, "redis-saturation:checkout-service", secondChange: true), [], Settings, DecisionId.New(), Now);

        decision.LeadingHypothesisId.ShouldBe(saturation, "which change caused it is not settled, so neither claims it");
    }

    [Fact]
    public void A_dependency_deployment_claims_the_regression_of_its_own_service_from_v4()
    {
        var regression = HypothesisId.New();
        Incident incident = WithDependencyRegression(regression, "dependency-regression:order-service");

        Decision current = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now);
        Decision v3 = Consensus.Decide(incident, [], Settings, DecisionId.New(), Now, ConsensusSettings.AlgorithmV3);

        current.AlgorithmVersion.ShouldBe(ConsensusSettings.AlgorithmV4);
        current.Result.ShouldBe(DecisionResult.Diagnosed);
        current.LeadingHypothesisId.ShouldBe(OrderChange);
        current.RejectedHypothesisIds.ShouldNotContain(regression);
        current.DissentingHypothesisIds.ShouldNotContain(regression);
        HypothesisAssessment change = current.Assessments.Single(assessment => assessment.HypothesisId == OrderChange);
        change.IndependentSupport.ShouldBe(4, "the deployment, the regression it caused and the two signals of that regression");
        change.Reason.ShouldBe("Stands, and explains dependency-regression:order-service.");
        v3.Result.ShouldBe(DecisionResult.Abstained, "v3 left the regression competing with the deployment that caused it, and replays that way");
    }

    [Fact]
    public void A_regression_blamed_on_another_service_is_not_claimed()
    {
        var regression = HypothesisId.New();

        Decision decision = Consensus.Decide(WithDependencyRegression(regression, "dependency-regression:payments-service"), [], Settings, DecisionId.New(), Now);

        decision.Result.ShouldBe(DecisionResult.Abstained);
        decision.Assessments.Single(assessment => assessment.HypothesisId == OrderChange).IndependentSupport.ShouldBe(2);
    }

    private static readonly HypothesisId Lead = HypothesisId.New();
    private static readonly HypothesisId Other = HypothesisId.New();
    private static readonly HypothesisId OrderChange = HypothesisId.New();

    /// <summary>
    /// S3 as the test double reports it: order-service v1.8.0 serves from 20 s and slows; investigators also propose that
    /// order-service regressed and checkout gave up waiting, nearly as strong; a benign checkout deployment serves from
    /// 140 s; the SLO burns from 160 s.
    /// </summary>
    private static Incident WithDependencyRegression(HypothesisId regression, string key)
    {
        (Incident incident, InvestigationId investigation) = Detected(
            160,
            Deployment("order-service", "v1.8.0", "v1.7.0", 0, 20, "d3"),
            Deployment("checkout-service", "v2.4.3", "v2.4.0", 120, 140, "d4"));
        var checkoutChange = HypothesisId.New();
        Evidence[] evidence =
        [
            Supporting(OrderChange, 0.5, 0, 20, "deploy-d3") with { IndependenceGroup = "deployment:d3" },
            Supporting(OrderChange, 0.6, 20, 160, "order-latency-1") with { IndependenceGroup = "latency:order-service" },
            Supporting(regression, 0.6, 20, 160, "dependency-wait-1") with { IndependenceGroup = "dependency-wait:checkout-service" },
            Supporting(regression, 0.4, 60, 160, "retries-1") with { IndependenceGroup = "retries:checkout-service" },
            Supporting(checkoutChange, 0.5, 120, 140, "deploy-d4") with { IndependenceGroup = "deployment:d4" }
        ];
        incident.RecordInvestigation(Report(investigation, evidence,
            Hypothesis(OrderChange, "The v1.8.0 deployment slowed order-service.", ServiceId.From("order-service"), ObservationId.From("deploy-controller:deployment:d3:started")) with { Key = "change:deploy-controller:deployment:d3:started" },
            Hypothesis(regression, "order-service regressed, and checkout-service gave up waiting for it.") with { Key = key },
            Hypothesis(checkoutChange, "The v2.4.3 deployment slowed checkout-service.", Checkout, ObservationId.From("deploy-controller:deployment:d4:started")) with { Key = "change:deploy-controller:deployment:d4:started" }), Start.AddSeconds(200)).IsSuccess.ShouldBeTrue();
        return incident;
    }

    /// <summary>
    /// The leak as live investigators reported it: the change rests on its deployment alone, while the saturation it caused
    /// is seen in three independent signals and outscores it on its own.
    /// </summary>
    private static Incident WithStrongSaturation(HypothesisId saturation, string key, bool secondChange = false)
    {
        LinkedChange[] changes = secondChange
            ? [Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1"), Deployment("checkout-service", "v2.4.2", "v2.4.1", 25, 30, "d5")]
            : [Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1")];
        (Incident incident, InvestigationId investigation) = Detected(60, changes);
        var second = HypothesisId.New();
        Evidence[] evidence =
        [
            Supporting(Lead, 0.5, 0, 20, "deploy-1", "deploy-2") with { IndependenceGroup = "deployment:d1" },
            Supporting(saturation, 0.85, 20, 50, "surge-1") with { IndependenceGroup = "redis-connections:checkout-service" },
            Supporting(saturation, 0.8, 30, 60, "errors-1") with { IndependenceGroup = "redis-errors:checkout-service" },
            Supporting(saturation, 0.7, 40, 60, "latency-1") with { IndependenceGroup = "latency:checkout-service" },
            .. secondChange ? [Supporting(second, 0.5, 25, 30, "deploy-5") with { IndependenceGroup = "deployment:d5" }] : Array.Empty<Evidence>()
        ];
        Hypothesis[] hypotheses =
        [
            Hypothesis(Lead, "The v2.4.1 deployment leaks connections.", Checkout, ObservationId.From("deploy-controller:deployment:d1:started")) with { Key = "change:deploy-controller:deployment:d1:started" },
            Hypothesis(saturation, "Redis ran out of client connections.") with { Key = key },
            .. secondChange
                ? [Hypothesis(second, "The v2.4.2 deployment leaks connections.", Checkout, ObservationId.From("deploy-controller:deployment:d5:started")) with { Key = "change:deploy-controller:deployment:d5:started" }]
                : Array.Empty<Hypothesis>()
        ];
        incident.RecordInvestigation(Report(investigation, evidence, hypotheses), Start.AddSeconds(200)).IsSuccess.ShouldBeTrue();
        return incident;
    }

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
