using Reference.Scenarios.Api.Contracts;
using Reference.Scenarios.Api.Manifests;
using Reference.Scenarios.Application.Runs;
using Reference.Scenarios.Domain.Scenarios;
using Shouldly;
using Reference.Checkout.Revisions;
using Reference.Orders.Revisions;
using Reference.QueueConsumer.Revisions;

namespace Reference.Tests;

public sealed class ScenarioManifestTests
{
    private static readonly ScenarioCatalog Catalog = ScenarioCatalogLoader.Load(Path.Combine(AppContext.BaseDirectory, "manifests"));

    private static readonly Dictionary<string, string> Baselines = new(StringComparer.Ordinal)
    {
        [CheckoutRevisions.ServiceName] = CheckoutRevisions.Baseline,
        [OrderRevisions.ServiceName] = OrderRevisions.Baseline,
        [QueueRevisions.ServiceName] = QueueRevisions.Baseline
    };

    public static TheoryData<string> ScenarioIds => new() { "S0", "S1", "S2", "S3", "S6", "S7", "S8" };

    [Fact]
    public void The_suite_contains_the_golden_case_five_confounders_and_a_control()
    {
        Catalog.Manifests.Keys.Order(StringComparer.Ordinal).ShouldBe(["S0", "S1", "S2", "S3", "S6", "S7", "S8"]);
        Catalog.Manifests["S6"].Kind.ShouldBe("confounder");
        Catalog.Manifests["S7"].Kind.ShouldBe("confounder");
        Catalog.Manifests["S8"].Kind.ShouldBe("confounder");
        Catalog.Manifests["S1"].Kind.ShouldBe("golden");
        Catalog.Manifests["S2"].Kind.ShouldBe("confounder");
        Catalog.Manifests["S3"].Kind.ShouldBe("confounder");
        Catalog.Manifests["S0"].Kind.ShouldBe("control");
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void Every_manifest_satisfies_the_scenario_contract(string scenarioId)
    {
        ScenarioCatalog.Validate(Catalog.Manifests[scenarioId]).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void The_recorded_baseline_outcome_follows_the_deploy_blame_rule(string scenarioId)
    {
        ScenarioManifest manifest = Catalog.Manifests[scenarioId];
        if (!manifest.Detection.ExpectIncident)
        {
            manifest.Baseline.Outcome.ShouldBe("noIncident");
            return;
        }

        // The rule blames the latest deployment to the affected service or its dependencies.
        string[] scope = [manifest.AffectedService, .. manifest.DownstreamServices];
        ScenarioStep? latest = manifest.Steps
            .Where(step => step.Action == "deploy" && scope.Contains(step.Service))
            .MaxBy(step => step.AtSeconds);

        if (latest is null)
        {
            manifest.Baseline.Outcome.ShouldBe("abstain");
            return;
        }

        manifest.Baseline.Outcome.ShouldBe("rollback");
        manifest.Baseline.Service.ShouldBe(latest.Service);
        manifest.Baseline.FromVersion.ShouldBe(latest.Version);
        manifest.Baseline.ToVersion.ShouldBe(Baselines[latest.Service]);
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void The_baseline_is_marked_correct_only_when_it_matches_a_permitted_remediation(string scenarioId)
    {
        ScenarioManifest manifest = Catalog.Manifests[scenarioId];
        bool matchesPermitted = manifest.Remediation.Permitted.Any(permitted =>
            permitted.Action == manifest.Baseline.Outcome && permitted.Service == manifest.Baseline.Service);
        bool isFalseRemediation = manifest.Remediation.FalseRemediations.Any(remediation =>
            remediation.Action == manifest.Baseline.Outcome && remediation.Service == manifest.Baseline.Service);

        if (manifest.Baseline.Outcome == "noIncident")
        {
            manifest.Baseline.Correct.ShouldBeTrue();
            return;
        }

        manifest.Baseline.Correct.ShouldBe(matchesPermitted);
        isFalseRemediation.ShouldBe(!manifest.Baseline.Correct);
    }

    [Fact]
    public void Confounder_deployments_are_the_most_recent_change_but_not_the_cause()
    {
        foreach (string scenarioId in new[] { "S2", "S3" })
        {
            ScenarioManifest manifest = Catalog.Manifests[scenarioId];
            ScenarioStep cause = manifest.Steps.Single(step => step.Role == "cause");
            ScenarioStep confounder = manifest.Steps.Single(step => step.Role == "confounder");

            confounder.AtSeconds.ShouldBeGreaterThan(cause.AtSeconds);
            confounder.Service.ShouldBe(CheckoutRevisions.ServiceName);
            manifest.Hypotheses!.Leading.Service.ShouldNotBe(CheckoutRevisions.ServiceName);
        }
    }

    [Fact]
    public void In_the_poison_message_the_deployment_follows_the_first_failure_and_the_cause_is_no_deployment()
    {
        ScenarioManifest manifest = Catalog.Manifests["S6"];
        ScenarioStep cause = manifest.Steps.Single(step => step.Role == "cause");
        ScenarioStep confounder = manifest.Steps.Single(step => step.Role == "confounder");

        cause.Action.ShouldBe("poisonMessage");
        confounder.Action.ShouldBe("deploy");
        confounder.Service.ShouldBe(manifest.AffectedService, "the innocent deployment is to the consumer itself");
        confounder.AtSeconds.ShouldBeGreaterThan(cause.AtSeconds);
        (confounder.AtSeconds + confounder.RolloutSeconds!.Value).ShouldBeLessThan(manifest.Detection.WithinSeconds, "it serves before the incident opens, so proximity favours it");
        manifest.Remediation.Permitted.ShouldHaveSingleItem().Action.ShouldBe("quarantine-message");
    }

    [Fact]
    public void Every_incident_scenario_names_discriminating_signals_and_a_recovery_window()
    {
        foreach (ScenarioManifest manifest in Catalog.Manifests.Values.Where(manifest => manifest.Detection.ExpectIncident))
        {
            manifest.DiscriminatingSignals.ShouldNotBeEmpty();
            manifest.Recovery!.ObservationWindowSeconds.ShouldBeGreaterThan(0);
            manifest.Recovery.EvaluationTimeoutSeconds.ShouldBeGreaterThan(manifest.Recovery.ObservationWindowSeconds);
        }
    }

    [Fact]
    public void A_manifest_missing_its_ground_truth_is_rejected()
    {
        ScenarioManifest incomplete = Catalog.Manifests["S1"] with { Cause = null, Hypotheses = null, DiscriminatingSignals = [] };

        ScenarioCatalog.Validate(incomplete).Count.ShouldBe(3);
    }
}
