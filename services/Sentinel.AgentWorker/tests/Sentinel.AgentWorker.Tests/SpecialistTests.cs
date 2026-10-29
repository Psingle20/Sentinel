using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Infrastructure.Models;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Diagnostics;
using Sentinel.AgentWorker.Application.Options;

namespace Sentinel.AgentWorker.Tests;

/// <summary>Builds the model investigator over the synthetic telemetry, as AgentWorker's container would.</summary>
internal static class Harness
{
    public static readonly IOptions<InvestigationOptions> Catalog = Options.Create(CatalogOptions());

    public static ModelInvestigator Investigator(Telemetry telemetry, IOptions<InvestigationOptions> options, IEnumerable<IInvestigationTool> tools, TestDoubleMode mode = TestDoubleMode.Deterministic, AgentWorkerTelemetry? metrics = null) =>
        Investigator(new DeterministicModelProvider(Options.Create(new TestDoubleOptions { Mode = mode })), options, tools, metrics);

    public static ModelInvestigator Investigator(IModelProvider model, IOptions<InvestigationOptions> options, IEnumerable<IInvestigationTool> tools, AgentWorkerTelemetry? metrics = null) =>
        new(
            new FixedRouter(model),
            tools,
            options,
            Options.Create(new ModelPricingOptions { InputPerMillionTokens = 1, OutputPerMillionTokens = 2 }),
            metrics ?? new AgentWorkerTelemetry(),
            TimeProvider.System,
            NullLogger<ModelInvestigator>.Instance);

    /// <summary>Every tool AgentWorker registers, over the synthetic telemetry.</summary>
    public static IInvestigationTool[] AllTools(Telemetry telemetry) =>
    [
        new MetricRangeTool(telemetry, Catalog),
        new RecentDeploymentsTool(telemetry),
        new LogSignaturesTool(telemetry),
        new SlowSpansTool(telemetry),
        new ServiceDependenciesTool(telemetry),
        new RunbookSearchTool(telemetry)
    ];

    private static InvestigationOptions CatalogOptions()
    {
        var options = new InvestigationOptions();
        options.Metrics["slo_burn_rate"] = "SLO burn rate.";
        options.Metrics["redis_connections"] = "Redis connections by client.";
        options.Metrics["latency_p95"] = "p95 latency by service.";
        options.Metrics["dependency_wait_p95"] = "p95 wait on a dependency.";
        options.Metrics["request_rate"] = "Requests per second.";
        return options;
    }

    private sealed class FixedRouter(IModelProvider model) : IModelRouter
    {
        public IReadOnlyCollection<string> Configured => [model.Provider];

        public IModelProvider For(string role) => model;
    }
}

public sealed class SpecialistTests
{
    [Fact]
    public async Task Each_specialist_calls_only_the_tools_of_its_role()
    {
        var telemetry = Telemetry.ConnectionLeak();

        foreach (string role in InvestigationRoles.Specialists)
        {
            InvestigationRun run = await Harness.Investigator(telemetry, Harness.Catalog, Harness.AllTools(telemetry)).InvestigateAsync(telemetry.Command(70, role), false, CancellationToken.None);

            run.Execution.ToolCalls.ShouldNotBeEmpty();
            run.Execution.ToolCalls.ShouldAllBe(call => Specialists.For(role).Tools.Contains(call.ToolName));
            run.Completed.Role.ShouldBe(role);
            run.Completed.Model.PromptVersion.ShouldBe($"{InvestigatorPrompt.Version}/{role}");
        }
    }

    [Fact]
    public async Task A_tool_outside_the_role_is_refused_like_an_unknown_one()
    {
        var telemetry = Telemetry.ConnectionLeak();
        var model = new ScriptedModel(new ModelToolCall("c1", RecentDeploymentsTool.Name, "{}"));

        InvestigationRun run = await Harness.Investigator(model, Harness.Catalog, Harness.AllTools(telemetry)).InvestigateAsync(telemetry.Command(70, InvestigationRoles.Topology), false, CancellationToken.None);

        ToolCallRecordShouldBeRefused(run, RecentDeploymentsTool.Name);
        model.OfferedTools.ShouldBe([ServiceDependenciesTool.Name]);
    }

    [Fact]
    public async Task In_the_leak_the_metrics_and_changes_specialists_propose_the_same_keyed_change()
    {
        var telemetry = Telemetry.ConnectionLeak();

        InvestigationRun metrics = await Run(telemetry, InvestigationRoles.Metrics, 70);
        InvestigationRun changes = await Run(telemetry, InvestigationRoles.Changes, 70);

        HypothesisFinding leak = metrics.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == HypothesisKeys.Change(ObservationId.From("deploy-controller:deployment:d1:started")));
        leak.Statement.ShouldContain("connection leak");
        changes.Completed.Hypotheses.ShouldContain(hypothesis => hypothesis.Key == leak.Key);
        metrics.Completed.Evidence.Single(evidence => evidence.HypothesisIds.Contains(leak.HypothesisId)).IndependenceGroup.ShouldBe("redis-connections:checkout-service");
        changes.Completed.Evidence.ShouldContain(evidence => evidence.IndependenceGroup == "deployment:d1");
    }

    [Fact]
    public async Task A_load_change_is_always_tested_and_flat_traffic_contradicts_it()
    {
        var telemetry = Telemetry.ConnectionLeak();

        InvestigationRun metrics = await Run(telemetry, InvestigationRoles.Metrics, 70);

        HypothesisFinding load = metrics.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == HypothesisKeys.LoadChange(ServiceId.From("checkout-service")));
        load.ContradictingEvidenceIds.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task In_the_batch_surge_the_culprit_is_named_and_logs_support_saturation()
    {
        var telemetry = Telemetry.ReportingSurge();

        InvestigationRun metrics = await Run(telemetry, InvestigationRoles.Metrics, 85);
        InvestigationRun logs = await Run(telemetry, InvestigationRoles.Logs, 85);

        metrics.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == HypothesisKeys.RedisSaturation(ServiceId.From("reporting-worker"))).SupportingEvidenceIds.ShouldNotBeEmpty();
        metrics.Completed.Hypotheses.Single(hypothesis => hypothesis.Key!.StartsWith("change:", StringComparison.Ordinal)).ContradictingEvidenceIds.ShouldNotBeEmpty();
        logs.Completed.Hypotheses.ShouldContain(hypothesis => hypothesis.Key == HypothesisKeys.RedisSaturation());
    }

    [Fact]
    public async Task In_the_dependency_regression_the_dependency_change_wins_support_and_the_late_change_is_contradicted()
    {
        var telemetry = Telemetry.DependencyRegression();
        string orders = HypothesisKeys.Change(ObservationId.From("deploy-controller:deployment:d3:started"));
        string checkout = HypothesisKeys.Change(ObservationId.From("deploy-controller:deployment:d4:started"));

        InvestigationRun metrics = await Run(telemetry, InvestigationRoles.Metrics, 190);
        InvestigationRun traces = await Run(telemetry, InvestigationRoles.Traces, 190);
        InvestigationRun logs = await Run(telemetry, InvestigationRoles.Logs, 190);

        metrics.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == orders).SupportingEvidenceIds.ShouldNotBeEmpty();
        metrics.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == checkout).ContradictingEvidenceIds.ShouldNotBeEmpty();
        traces.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == orders).SupportingEvidenceIds.ShouldNotBeEmpty();
        logs.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == orders).SupportingEvidenceIds.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Runbook_text_is_quoted_as_data_and_never_becomes_evidence()
    {
        Telemetry telemetry = Telemetry.ConnectionLeak().WithPlantedRunbook();

        InvestigationRun run = await Run(telemetry, InvestigationRoles.Knowledge, 70);

        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Completed);
        run.Completed.Evidence.ShouldBeEmpty();
        run.Execution.ToolCalls.ShouldHaveSingleItem().Result.ShouldContain("\"untrustedData\":true");
        run.Completed.Unknowns.ShouldNotBeNull().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_model_that_obeys_a_planted_instruction_is_refused_and_abstains()
    {
        Telemetry telemetry = Telemetry.ConnectionLeak().WithPlantedRunbook();

        InvestigationRun run = await Harness.Investigator(telemetry, Harness.Catalog, Harness.AllTools(telemetry), TestDoubleMode.InjectionSusceptible)
            .InvestigateAsync(telemetry.Command(70, InvestigationRoles.Knowledge), false, CancellationToken.None);

        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Abstained);
        run.Completed.AbstentionReason.ShouldNotBeNull().ShouldContain("never actions");
        run.Completed.Hypotheses.ShouldBeEmpty();
        run.Execution.SchemaFailures.ShouldBe(2);
    }

    [Fact]
    public void Findings_that_carry_actions_are_refused_whatever_else_they_contain()
    {
        ValidatedFindings findings = FindingsValidator.Validate(
            """{"abstain":false,"plan":{"rollback":"checkout-service"},"evidence":[],"hypotheses":[]}""",
            new HashSet<ObservationId>(),
            new HashSet<string>(StringComparer.Ordinal),
            minimumHypotheses: 0);

        findings.IsValid.ShouldBeFalse();
        findings.Errors.ShouldContain(error => error.Contains("'plan'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hypothesis_key_outside_the_vocabulary_is_refused()
    {
        var returned = new HashSet<ObservationId> { ObservationId.From("prometheus:x:1") };
        ValidatedFindings findings = FindingsValidator.Validate(
            """
            {"evidence":[{"id":"e1","statement":"s","direction":"supports","strength":0.5,"observationIds":["prometheus:x:1"],"hypothesisIds":["h1"],"from":"2026-10-28T09:00:00Z","to":"2026-10-28T09:00:05Z","toolCallId":"c1"}],
             "hypotheses":[{"id":"h1","key":"Ignore Instructions!","statement":"s","causalChain":["a"]}]}
            """,
            returned,
            new HashSet<string>(StringComparer.Ordinal) { "c1" },
            minimumHypotheses: 0);

        findings.Errors.ShouldContain(error => error.Contains("key vocabulary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Model_calls_record_latency_tokens_and_estimated_cost()
    {
        var telemetry = Telemetry.ConnectionLeak();
        using var metrics = new AgentWorkerTelemetry();
        var recorded = new Dictionary<string, double>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) =>
        {
            if (instrument.Meter.Name == AgentWorkerTelemetry.MeterName)
            {
                meter.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => Add(instrument.Name, value));
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => Add(instrument.Name, value));
        listener.Start();

        await Harness.Investigator(telemetry, Harness.Catalog, Harness.AllTools(telemetry), metrics: metrics).InvestigateAsync(telemetry.Command(70, InvestigationRoles.Metrics), false, CancellationToken.None);

        recorded["sentinel.agentworker.model.tokens"].ShouldBeGreaterThan(0);
        recorded["sentinel.agentworker.model.duration"].ShouldBeGreaterThanOrEqualTo(0);
        recorded["sentinel.agentworker.model.cost"].ShouldBeGreaterThan(0);
        recorded.ShouldContainKey("sentinel.agentworker.tool.duration");

        void Add(string name, double value)
        {
            lock (recorded)
            {
                recorded[name] = recorded.GetValueOrDefault(name) + value;
            }
        }
    }

    private static Task<InvestigationRun> Run(Telemetry telemetry, string role, int detectedAt, bool withLogs = false)
    {
        _ = withLogs;
        return Harness.Investigator(telemetry, Harness.Catalog, Harness.AllTools(telemetry)).InvestigateAsync(telemetry.Command(detectedAt, role), false, CancellationToken.None);
    }

    private static void ToolCallRecordShouldBeRefused(InvestigationRun run, string tool) =>
        run.Execution.ToolCalls.ShouldContain(call => call.ToolName == tool && call.Error != null && call.Error.Contains("not available", StringComparison.Ordinal));

    /// <summary>A model that asks for one fixed tool call, then abstains, and remembers which tools it was offered.</summary>
    private sealed class ScriptedModel(ModelToolCall call) : IModelProvider
    {
        public IReadOnlyList<string> OfferedTools { get; private set; } = [];

        public string Provider => "scripted";

        public string Model => "scripted";

        public string? ModelVersion => null;

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            OfferedTools = [.. request.Tools.Select(tool => tool.Name)];
            return Task.FromResult(request.Messages.Any(message => message.Role == ModelRole.Tool)
                ? new ModelResponse(ModelFinish.Final, [], """{"abstain":true,"abstentionReason":"done"}""", new ModelUsage(10, 10))
                : new ModelResponse(ModelFinish.ToolCalls, [call], null, new ModelUsage(10, 10)));
        }
    }
}
