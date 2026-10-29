using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

public sealed class VerifierTests
{
    private static readonly ServiceId Checkout = ServiceId.From("checkout-service");
    private static readonly ServiceId Orders = ServiceId.From("order-service");
    private static readonly ServiceId Reporting = ServiceId.From("reporting-worker");

    [Fact]
    public async Task In_the_batch_surge_the_rolling_deploy_is_falsified_and_the_batch_survives()
    {
        var telemetry = Telemetry.ReportingSurge();
        HypothesisFinding deploy = Change("d2");
        HypothesisFinding batch = Hypothesis(HypothesisKeys.RedisSaturation(Reporting), Reporting);
        HypothesisFinding load = Hypothesis(HypothesisKeys.LoadChange(Checkout), Checkout);

        InvestigationRun run = await Verify(telemetry, 85, deploy, batch, load);

        Verdict(run, deploy, AdversarialVerifier.ChangeServing).ShouldBe(ChallengeVerdict.Falsified);
        Verdict(run, batch, AdversarialVerifier.CulpritPrecedesOnset).ShouldBe(ChallengeVerdict.Survived);
        run.Completed.Role.ShouldBe(InvestigationRoles.Verifier);
        run.Completed.Evidence.Single(evidence => evidence.HypothesisIds.Contains(deploy.HypothesisId)).IndependenceGroup.ShouldBe("deployment:d2");
    }

    [Fact]
    public async Task In_the_leak_the_deploy_survives_both_falsifiers()
    {
        var telemetry = Telemetry.ConnectionLeak();
        HypothesisFinding leak = Change("d1");

        InvestigationRun run = await Verify(telemetry, 70, leak);

        Verdict(run, leak, AdversarialVerifier.ChangeServing).ShouldBe(ChallengeVerdict.Survived);
        Verdict(run, leak, AdversarialVerifier.MechanismFollowsChange).ShouldBe(ChallengeVerdict.Survived);
        run.Completed.Evidence.Single(evidence => evidence.HypothesisIds.Contains(leak.HypothesisId)).IndependenceGroup.ShouldBe("redis-connections:checkout-service");
    }

    [Fact]
    public async Task A_change_replaced_by_a_later_deployment_before_onset_is_falsified_while_the_serving_change_survives()
    {
        var telemetry = Telemetry.ReplacedChangeThenLeak();
        HypothesisFinding replaced = Change("d6");
        HypothesisFinding leak = Change("d1");

        InvestigationRun run = await Verify(telemetry, 70, replaced, leak);

        Verdict(run, replaced, AdversarialVerifier.ChangeServing).ShouldBe(ChallengeVerdict.Survived);
        Verdict(run, replaced, AdversarialVerifier.ChangeStillServing).ShouldBe(ChallengeVerdict.Falsified);
        Verdict(run, leak, AdversarialVerifier.ChangeStillServing).ShouldBe(ChallengeVerdict.Survived);
        run.Completed.Evidence.ShouldContain(evidence => evidence.HypothesisIds.Contains(replaced.HypothesisId)
            && evidence.SourceObservationIds.Contains(ObservationId.From("deploy-controller:deployment:d7:completed")));
    }

    [Fact]
    public async Task An_earlier_leak_in_the_window_does_not_hide_the_mechanism_of_the_change_that_is_serving()
    {
        var telemetry = Telemetry.RepeatedLeak();
        HypothesisFinding earlier = Change("d8");
        HypothesisFinding current = Change("d1");

        InvestigationRun run = await Verify(telemetry, 70, earlier, current);

        Verdict(run, earlier, AdversarialVerifier.ChangeStillServing).ShouldBe(ChallengeVerdict.Falsified);
        Verdict(run, current, AdversarialVerifier.MechanismFollowsChange).ShouldBe(ChallengeVerdict.Survived);
        run.Completed.Evidence.Where(evidence => evidence.HypothesisIds.Contains(current.HypothesisId)).ShouldAllBe(evidence => evidence.Direction == EvidenceDirection.Supports);
    }

    [Fact]
    public async Task In_the_dependency_regression_only_the_dependency_change_shows_a_mechanism()
    {
        var telemetry = Telemetry.DependencyRegression();
        HypothesisFinding orders = Change("d3", Orders);
        HypothesisFinding checkout = Change("d4");

        InvestigationRun run = await Verify(telemetry, 190, orders, checkout);

        Verdict(run, orders, AdversarialVerifier.MechanismFollowsChange).ShouldBe(ChallengeVerdict.Survived);
        Verdict(run, checkout, AdversarialVerifier.ChangeServing).ShouldBe(ChallengeVerdict.Survived);
        Verdict(run, checkout, AdversarialVerifier.MechanismFollowsChange).ShouldBe(ChallengeVerdict.Inconclusive);
    }

    [Fact]
    public async Task A_strong_signal_no_hypothesis_explains_is_added_as_an_alternative()
    {
        var telemetry = Telemetry.ReportingSurge();

        InvestigationRun run = await Verify(telemetry, 85, Change("d2"));

        HypothesisFinding alternative = run.Completed.Hypotheses.ShouldHaveSingleItem();
        alternative.Key.ShouldBe(HypothesisKeys.RedisSaturation(Reporting));
        alternative.SupportingEvidenceIds.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Flat_load_falsifies_a_load_explanation_and_an_untestable_one_stays_inconclusive()
    {
        var telemetry = Telemetry.ConnectionLeak();
        HypothesisFinding load = Hypothesis(HypothesisKeys.LoadChange(Checkout), Checkout);
        HypothesisFinding vague = Hypothesis(HypothesisKeys.ExternalDependency, null);

        InvestigationRun run = await Verify(telemetry, 70, load, vague);

        Verdict(run, load, AdversarialVerifier.LoadRose).ShouldBe(ChallengeVerdict.Falsified);
        Verdict(run, vague, AdversarialVerifier.Untestable).ShouldBe(ChallengeVerdict.Inconclusive);
    }

    [Fact]
    public async Task Every_challenge_cites_evidence_from_recorded_reads()
    {
        var telemetry = Telemetry.ReportingSurge();

        InvestigationRun run = await Verify(telemetry, 85, Change("d2"), Hypothesis(HypothesisKeys.RedisSaturation(Reporting), Reporting));

        HashSet<string> calls = [.. run.Execution.ToolCalls.Select(call => call.ToolCallId)];
        run.Completed.Evidence.ShouldAllBe(evidence => calls.Contains(evidence.ToolCallId));
        HashSet<ObservationId> read = [.. run.Execution.ToolCalls.SelectMany(call => call.ObservationIds)];
        run.Completed.Evidence.SelectMany(evidence => evidence.SourceObservationIds).ShouldAllBe(id => read.Contains(id));
    }

    private static async Task<InvestigationRun> Verify(Telemetry telemetry, int detectedAt, params HypothesisFinding[] hypotheses) =>
        await new AdversarialVerifier(telemetry, TimeProvider.System).InvestigateAsync(
            telemetry.Command(detectedAt, InvestigationRoles.Verifier) with { Hypotheses = hypotheses, Evidence = [] },
            false,
            CancellationToken.None);

    private static ChallengeVerdict Verdict(InvestigationRun run, HypothesisFinding hypothesis, string falsifier) =>
        run.Completed.Challenges.ShouldNotBeNull().Single(challenge => challenge.HypothesisId == hypothesis.HypothesisId && challenge.Falsifier == falsifier).Verdict;

    private static HypothesisFinding Change(string deployment, ServiceId? service = null)
    {
        var change = ObservationId.From($"deploy-controller:deployment:{deployment}:started");
        return new HypothesisFinding
        {
            HypothesisId = HypothesisId.New(),
            Key = HypothesisKeys.Change(change),
            Statement = $"The {deployment} deployment caused the symptoms.",
            CausalChain = ["deploy", "SLO burn"],
            SuspectedServiceId = service ?? Checkout,
            SuspectedChangeObservationId = change
        };
    }

    private static HypothesisFinding Hypothesis(string key, ServiceId? service) => new()
    {
        HypothesisId = HypothesisId.New(),
        Key = key,
        Statement = $"Explanation {key}.",
        CausalChain = ["cause", "SLO burn"],
        SuspectedServiceId = service
    };
}
