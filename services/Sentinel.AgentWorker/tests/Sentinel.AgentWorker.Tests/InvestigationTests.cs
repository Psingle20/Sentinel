using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Infrastructure.Models;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

public sealed class InvestigationTests
{
    private static readonly IOptions<InvestigationOptions> Options = Microsoft.Extensions.Options.Options.Create(CreateOptions());

    private static InvestigationOptions CreateOptions()
    {
        var options = new InvestigationOptions();
        options.Metrics["slo_burn_rate"] = "SLO burn rate.";
        options.Metrics["redis_connections"] = "Redis connections by client.";
        return options;
    }

    private static ModelInvestigator Investigator(Telemetry telemetry, TestDoubleMode mode = TestDoubleMode.Deterministic) =>
        Harness.Investigator(telemetry, Options, [new MetricRangeTool(telemetry, Options), new RecentDeploymentsTool(telemetry)], mode);

    [Fact]
    public async Task In_the_connection_leak_the_leaking_deployment_is_linked_to_the_connection_surge()
    {
        var telemetry = Telemetry.ConnectionLeak();

        InvestigationRun run = await Investigator(telemetry).InvestigateAsync(telemetry.Command(detectedAt: 70), shadow: false, CancellationToken.None);

        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Completed);
        run.Completed.Hypotheses.Count.ShouldBeGreaterThanOrEqualTo(2);
        HypothesisFinding leak = run.Completed.Hypotheses.Single(hypothesis => hypothesis.Statement.Contains("connection leak", StringComparison.Ordinal));
        leak.SuspectedServiceId.ShouldBe(ServiceId.From("checkout-service"));
        leak.SuspectedChangeObservationId.ShouldNotBeNull();
        run.Completed.Evidence.Where(evidence => evidence.HypothesisIds.Contains(leak.HypothesisId))
            .SelectMany(evidence => evidence.SourceObservationIds).Distinct().Count().ShouldBeGreaterThanOrEqualTo(2);
        run.Execution.ToolCalls.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task In_the_reporting_surge_the_benign_deployment_is_contradicted_and_the_batch_is_a_hypothesis()
    {
        var telemetry = Telemetry.ReportingSurge();

        InvestigationRun run = await Investigator(telemetry).InvestigateAsync(telemetry.Command(detectedAt: 80), shadow: false, CancellationToken.None);

        HypothesisFinding surge = run.Completed.Hypotheses.Single(hypothesis => hypothesis.SuspectedServiceId == ServiceId.From("reporting-worker"));
        HypothesisFinding deploy = run.Completed.Hypotheses.Single(hypothesis => hypothesis.SuspectedChangeObservationId is not null);
        deploy.ContradictingEvidenceIds.ShouldNotBeEmpty();
        surge.SupportingEvidenceIds.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(TestDoubleMode.Malformed)]
    [InlineData(TestDoubleMode.Incomplete)]
    [InlineData(TestDoubleMode.Fabricated)]
    [InlineData(TestDoubleMode.Refusal)]
    public async Task Invalid_or_refused_output_leads_to_abstention_and_never_to_findings(TestDoubleMode mode)
    {
        var telemetry = Telemetry.ConnectionLeak();

        InvestigationRun run = await Investigator(telemetry, mode).InvestigateAsync(telemetry.Command(detectedAt: 70), shadow: false, CancellationToken.None);

        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Abstained);
        run.Completed.Evidence.ShouldBeEmpty();
        run.Completed.Hypotheses.ShouldBeEmpty();
        run.Completed.AbstentionReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_model_that_never_answers_is_stopped_by_the_time_budget()
    {
        var telemetry = Telemetry.ConnectionLeak();
        InvestigateIncident command = telemetry.Command(detectedAt: 70) with { Budget = new InvestigationBudget(12, 40_000, TimeSpan.FromMilliseconds(200)) };

        InvestigationRun run = await Investigator(telemetry, TestDoubleMode.Timeout).InvestigateAsync(command, shadow: false, CancellationToken.None);

        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Abstained);
        run.Completed.AbstentionReason!.ShouldContain("time budget");
    }

    [Fact]
    public async Task Evidence_from_partial_data_is_marked_partial()
    {
        var complete = Telemetry.ConnectionLeak();
        var partial = Telemetry.ConnectionLeak(partial: true);

        InvestigationRun fromComplete = await Investigator(complete).InvestigateAsync(complete.Command(detectedAt: 70), shadow: false, CancellationToken.None);
        InvestigationRun fromPartial = await Investigator(partial).InvestigateAsync(partial.Command(detectedAt: 70), shadow: false, CancellationToken.None);

        fromComplete.Completed.Evidence.ShouldAllBe(evidence => !evidence.Partial);
        fromPartial.Completed.Evidence.ShouldNotBeEmpty();
        fromPartial.Completed.Evidence.ShouldAllBe(evidence => evidence.Partial);
    }

    [Fact]
    public async Task The_deploy_blame_baseline_blames_the_latest_deployment_even_when_it_is_innocent()
    {
        var telemetry = Telemetry.ReportingSurge();

        InvestigationRun run = await new DeployBlameInvestigator(telemetry, TimeProvider.System)
            .InvestigateAsync(telemetry.Command(detectedAt: 80), shadow: true, CancellationToken.None);

        run.Execution.Shadow.ShouldBeTrue();
        HypothesisFinding blamed = run.Completed.Hypotheses[0];
        blamed.SuspectedServiceId.ShouldBe(ServiceId.From("checkout-service"));
        blamed.Statement.ShouldContain("v2.4.2");
        run.Completed.Model.Model.ShouldBe(DeployBlameInvestigator.InvestigatorName);
    }
}
