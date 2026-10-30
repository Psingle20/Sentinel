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
    public async Task In_the_poison_message_metrics_and_logs_name_the_message_and_contradict_the_later_deployment()
    {
        var telemetry = Telemetry.PoisonMessage();
        string poison = HypothesisKeys.PoisonMessage(ServiceId.From("queue-consumer"));
        string deployment = HypothesisKeys.Change(ObservationId.From("deploy-controller:deployment:d10:started"));

        InvestigationRun metrics = await Run(telemetry, InvestigationRoles.Metrics, 70);
        InvestigationRun logs = await Run(telemetry, InvestigationRoles.Logs, 70);
        InvestigationRun changes = await Run(telemetry, InvestigationRoles.Changes, 70);

        foreach (InvestigationRun run in new[] { metrics, logs })
        {
            HypothesisFinding cause = run.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == poison);
            cause.SuspectedServiceId.ShouldBe(ServiceId.From("queue-consumer"));
            cause.SupportingEvidenceIds.ShouldNotBeEmpty();
            run.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == deployment).ContradictingEvidenceIds.ShouldNotBeEmpty();
        }

        metrics.Completed.Evidence.ShouldContain(evidence => evidence.Statement.Contains("kept processing the other messages", StringComparison.Ordinal));
        logs.Completed.Evidence.ShouldContain(evidence => evidence.Statement.Contains("failures of message ord-poison-1", StringComparison.Ordinal));
        EvidenceFinding proximity = changes.Completed.Evidence.Single(evidence => evidence.HypothesisIds.Contains(changes.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == deployment).HypothesisId));
        proximity.Strength.ShouldBeLessThan(0.3, "the deployment served after the symptoms began, so proximity is weak");
    }

    [Fact]
    public async Task In_retry_amplification_every_specialist_blames_the_slow_dependency_and_the_retries_support_it()
    {
        var telemetry = Telemetry.RetryAmplification();
        string orders = HypothesisKeys.DependencyRegression(ServiceId.From("order-service"));
        string deployment = HypothesisKeys.Change(ObservationId.From("deploy-controller:deployment:d12:started"));

        InvestigationRun metrics = await Run(telemetry, InvestigationRoles.Metrics, 120);
        InvestigationRun logs = await Run(telemetry, InvestigationRoles.Logs, 120);
        InvestigationRun traces = await Run(telemetry, InvestigationRoles.Traces, 120);

        foreach (InvestigationRun run in new[] { metrics, logs, traces })
        {
            HypothesisFinding cause = run.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == orders);
            cause.SuspectedServiceId.ShouldBe(ServiceId.From("order-service"));
            cause.SupportingEvidenceIds.ShouldNotBeEmpty();
            run.Completed.Hypotheses.ShouldNotContain(hypothesis => hypothesis.Key == HypothesisKeys.PoisonMessage(ServiceId.From("queue-consumer")));
        }

        metrics.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == deployment).ContradictingEvidenceIds.ShouldNotBeEmpty();
        metrics.Completed.Evidence.ShouldContain(evidence => evidence.Statement.Contains("the retries add to order-service's load", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicates_that_ended_before_the_symptoms_began_are_not_a_cause()
    {
        Telemetry telemetry = Telemetry.RetryAmplification().WithEarlierDuplicates();
        string duplicates = HypothesisKeys.DuplicateDelivery(ServiceId.From("queue-consumer"));

        InvestigationRun metrics = await Run(telemetry, InvestigationRoles.Metrics, 120);
        InvestigationRun logs = await Run(telemetry, InvestigationRoles.Logs, 120);

        metrics.Completed.Hypotheses.ShouldNotContain(hypothesis => hypothesis.Key == duplicates);
        logs.Completed.Hypotheses.ShouldNotContain(hypothesis => hypothesis.Key == duplicates);
        metrics.Completed.Hypotheses.ShouldContain(hypothesis => hypothesis.Key == HypothesisKeys.DependencyRegression(ServiceId.From("order-service")));
    }

    [Fact]
    public async Task In_duplicate_delivery_metrics_and_logs_find_orders_booked_twice_without_redeliveries_and_contradict_the_later_deployment()
    {
        var telemetry = Telemetry.DuplicateDelivery();
        string duplicates = HypothesisKeys.DuplicateDelivery(ServiceId.From("queue-consumer"));
        string deployment = HypothesisKeys.Change(ObservationId.From("deploy-controller:deployment:d11:started"));

        InvestigationRun metrics = await Run(telemetry, InvestigationRoles.Metrics, 70);
        InvestigationRun logs = await Run(telemetry, InvestigationRoles.Logs, 70);

        foreach (InvestigationRun run in new[] { metrics, logs })
        {
            HypothesisFinding cause = run.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == duplicates);
            cause.SuspectedServiceId.ShouldBe(ServiceId.From("queue-consumer"));
            cause.SupportingEvidenceIds.ShouldNotBeEmpty();
            run.Completed.Hypotheses.Single(hypothesis => hypothesis.Key == deployment).ContradictingEvidenceIds.ShouldNotBeEmpty();
            run.Completed.Hypotheses.ShouldNotContain(hypothesis => hypothesis.Key == HypothesisKeys.PoisonMessage(ServiceId.From("queue-consumer")));
        }

        metrics.Completed.Evidence.ShouldContain(evidence => evidence.Statement.Contains("not redeliveries", StringComparison.Ordinal));
        logs.Completed.Evidence.ShouldContain(evidence => evidence.Statement.Contains("orders booked again from a second message", StringComparison.Ordinal));
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
    public async Task Log_text_is_quoted_as_data_and_never_steers_the_findings()
    {
        var clean = Telemetry.ConnectionLeak();
        Telemetry planted = Telemetry.ConnectionLeak().WithPlantedLog();

        InvestigationRun baseline = await Run(clean, InvestigationRoles.Logs, 70);
        InvestigationRun run = await Run(planted, InvestigationRoles.Logs, 70);

        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Completed);
        run.Execution.ToolCalls.ShouldContain(call => call.Result != null && call.Result.Contains("\"untrustedData\":true", StringComparison.Ordinal) && call.Result.Contains("SYSTEM NOTE TO AI AGENTS", StringComparison.Ordinal));
        run.Completed.Hypotheses.Select(hypothesis => hypothesis.Key).ShouldBe(baseline.Completed.Hypotheses.Select(hypothesis => hypothesis.Key));
        run.Completed.Hypotheses.ShouldAllBe(hypothesis => !hypothesis.Statement.Contains("roll back every service", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_model_that_obeys_an_instruction_planted_in_logs_is_refused_and_abstains()
    {
        Telemetry telemetry = Telemetry.ConnectionLeak().WithPlantedLog();

        InvestigationRun run = await Harness.Investigator(telemetry, Harness.Catalog, Harness.AllTools(telemetry), TestDoubleMode.InjectionSusceptible)
            .InvestigateAsync(telemetry.Command(70, InvestigationRoles.Logs), false, CancellationToken.None);

        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Abstained);
        run.Completed.AbstentionReason.ShouldNotBeNull().ShouldContain("never actions");
        run.Completed.Hypotheses.ShouldBeEmpty();
        run.Completed.Evidence.ShouldBeEmpty();
        run.Execution.SchemaFailures.ShouldBe(2);
    }

    [Theory]
    [InlineData(TestDoubleMode.Refusal, "sentinel.agentworker.model.failures")]
    [InlineData(TestDoubleMode.Malformed, "sentinel.agentworker.model.schema_failures")]
    public async Task Refused_calls_and_rejected_outputs_are_counted(TestDoubleMode mode, string counter)
    {
        var telemetry = Telemetry.ConnectionLeak();
        using var metrics = new AgentWorkerTelemetry();
        var recorded = new Dictionary<string, long>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) =>
        {
            if (instrument.Meter.Name == AgentWorkerTelemetry.MeterName)
            {
                meter.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            lock (recorded)
            {
                recorded[instrument.Name] = recorded.GetValueOrDefault(instrument.Name) + value;
            }
        });
        listener.Start();

        InvestigationRun run = await Harness.Investigator(telemetry, Harness.Catalog, Harness.AllTools(telemetry), mode, metrics)
            .InvestigateAsync(telemetry.Command(70, InvestigationRoles.Metrics), false, CancellationToken.None);

        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Abstained);
        recorded.GetValueOrDefault(counter).ShouldBeGreaterThan(0);
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
}
