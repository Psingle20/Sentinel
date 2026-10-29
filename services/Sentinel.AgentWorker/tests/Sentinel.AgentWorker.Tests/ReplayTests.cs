using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.AgentWorker.Infrastructure.Models;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Shouldly;
using Sentinel.AgentWorker.Application.Abstractions.Persistence;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Capture;
using Sentinel.AgentWorker.Application.Diagnostics;
using Sentinel.AgentWorker.Application.Options;
using Sentinel.AgentWorker.Application.BusinessTasks.Commands.Replays;

namespace Sentinel.AgentWorker.Tests;

/// <summary>Phase 07 replay levels 2 and 3: captured provider responses reproduce an investigation without any live provider.</summary>
public sealed class ReplayTests
{
    [Fact]
    public async Task A_deterministic_investigation_replays_from_its_captured_inputs_alone()
    {
        var telemetry = Telemetry.ConnectionLeak();
        var store = new InMemoryExecutionStore();
        AgentExecutionRecord execution = await RecordAsync(telemetry, store, new DeterministicModelProvider(Options.Create(new TestDoubleOptions())), InvestigationRoles.Metrics);
        var offline = new OfflineProviders();

        InvestigationReplayResult replay = (await Replayer(store, offline, new DeterministicModelProvider(Options.Create(new TestDoubleOptions())))
            .Handle(new ReplayInvestigationCommand(execution.AgentExecutionId, new InvestigationReplayRequest()), CancellationToken.None)).Value!;

        replay.Level.ShouldBe(ReplayLevel.Investigation);
        replay.InputsReproduced.ShouldBeTrue(string.Join("; ", replay.ToolCallMismatches.Select(mismatch => mismatch.Reason)));
        replay.ToolCallsReplayed.ShouldBe(execution.ToolCalls.Count);
        replay.UncapturedQueries.ShouldBe(0);
        replay.FindingsMatch.ShouldBe(true, string.Join("; ", replay.Differences));
        replay.Deterministic.ShouldBeTrue();
        replay.UnsupportedCitations.ShouldBeEmpty();
        offline.Calls.ShouldBe(0);
        store.Replays.ShouldHaveSingleItem().ReplayId.ShouldBe(replay.ReplayId);
    }

    [Fact]
    public async Task A_live_model_is_not_called_at_level_2_so_only_its_inputs_are_reproduced()
    {
        var telemetry = Telemetry.ConnectionLeak();
        var store = new InMemoryExecutionStore();
        var live = new NondeterministicModel(new DeterministicModelProvider(Options.Create(new TestDoubleOptions())));
        AgentExecutionRecord execution = await RecordAsync(telemetry, store, live, InvestigationRoles.Metrics);
        int callsBefore = live.Calls;

        InvestigationReplayResult replay = (await Replayer(store, new OfflineProviders(), live)
            .Handle(new ReplayInvestigationCommand(execution.AgentExecutionId, new InvestigationReplayRequest()), CancellationToken.None)).Value!;

        live.Calls.ShouldBe(callsBefore);
        replay.InputsReproduced.ShouldBeTrue();
        replay.FindingsMatch.ShouldBeNull();
        replay.Replayed.ShouldBeNull();
        replay.Explanation.ShouldContain("not deterministic");
    }

    [Fact]
    public async Task Model_reevaluation_reruns_frozen_inputs_with_another_prompt_version_and_says_whether_that_is_reproducible()
    {
        var telemetry = Telemetry.ConnectionLeak();
        var store = new InMemoryExecutionStore();
        AgentExecutionRecord execution = await RecordAsync(telemetry, store, new DeterministicModelProvider(Options.Create(new TestDoubleOptions())), InvestigationRoles.Metrics);

        InvestigationReplayResult replay = (await Replayer(store, new OfflineProviders(), new DeterministicModelProvider(Options.Create(new TestDoubleOptions())))
            .Handle(new ReplayInvestigationCommand(execution.AgentExecutionId, new InvestigationReplayRequest { Level = ReplayLevel.ModelReevaluation, PromptVersion = InvestigatorPrompt.V2 }), CancellationToken.None)).Value!;

        replay.Level.ShouldBe(ReplayLevel.ModelReevaluation);
        replay.Replayed.ShouldNotBeNull().Model.PromptVersion.ShouldBe($"{InvestigatorPrompt.V2}/{InvestigationRoles.Metrics}");
        replay.Deterministic.ShouldBeTrue();
        replay.UnsupportedCitations.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_query_outside_the_captured_inputs_fails_explicitly_and_never_reaches_a_live_provider()
    {
        var telemetry = Telemetry.ConnectionLeak();
        var store = new InMemoryExecutionStore();
        AgentExecutionRecord execution = await RecordAsync(telemetry, store, new DeterministicModelProvider(Options.Create(new TestDoubleOptions())), InvestigationRoles.Metrics);
        ExecutionInputs inputs = (await store.GetInputsAsync(execution.AgentExecutionId, CancellationToken.None))!;
        await store.SaveInputsAsync(inputs with { Responses = [.. inputs.Responses.Skip(1)] }, CancellationToken.None);
        var offline = new OfflineProviders();

        InvestigationReplayResult replay = (await Replayer(store, offline, new DeterministicModelProvider(Options.Create(new TestDoubleOptions())))
            .Handle(new ReplayInvestigationCommand(execution.AgentExecutionId, new InvestigationReplayRequest()), CancellationToken.None)).Value!;

        replay.InputsReproduced.ShouldBeFalse();
        replay.UncapturedQueries.ShouldBeGreaterThan(0);
        replay.Deterministic.ShouldBeFalse();
        offline.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_adversarial_verifier_is_code_and_replays_exactly()
    {
        var telemetry = Telemetry.ConnectionLeak();
        var store = new InMemoryExecutionStore();
        InvestigateIncident command = await ChallengeCommandAsync(telemetry);
        var capture = new InvestigationCapture();
        var verifier = new AdversarialVerifier(new CapturingObservationQueries(telemetry, capture), TimeProvider.System);
        capture.BeginRecording();
        InvestigationRun run = await verifier.InvestigateAsync(command, false, CancellationToken.None);
        await SaveRunAsync(store, run, command, capture.EndRecording());

        var replayCapture = new InvestigationCapture();
        var offline = new OfflineProviders();
        var replayer = new ReplayInvestigationCommandHandler(
            store,
            [new AdversarialVerifier(new CapturingObservationQueries(offline, replayCapture), TimeProvider.System)],
            [],
            new FixedRouter(new DeterministicModelProvider(Options.Create(new TestDoubleOptions()))),
            replayCapture,
            TimeProvider.System,
            NullLogger<ReplayInvestigationCommandHandler>.Instance);
        InvestigationReplayResult replay = (await replayer.Handle(new ReplayInvestigationCommand(run.Execution.AgentExecutionId, new InvestigationReplayRequest()), CancellationToken.None)).Value!;

        replay.FindingsMatch.ShouldBe(true, string.Join("; ", replay.Differences));
        replay.Deterministic.ShouldBeTrue();
        offline.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_execution_recorded_without_inputs_says_it_cannot_be_replayed()
    {
        var telemetry = Telemetry.ConnectionLeak();
        var store = new InMemoryExecutionStore();
        AgentExecutionRecord execution = await RecordAsync(telemetry, store, new DeterministicModelProvider(Options.Create(new TestDoubleOptions())), InvestigationRoles.Metrics);
        store.ForgetInputs(execution.AgentExecutionId);

        InvestigationReplayResult replay = (await Replayer(store, new OfflineProviders(), new DeterministicModelProvider(Options.Create(new TestDoubleOptions())))
            .Handle(new ReplayInvestigationCommand(execution.AgentExecutionId, new InvestigationReplayRequest()), CancellationToken.None)).Value!;

        replay.InputsReproduced.ShouldBeFalse();
        replay.Deterministic.ShouldBeFalse();
        replay.Explanation.ShouldContain("before its inputs were captured");
    }

    [Fact]
    public void Prompt_v3_offers_similar_incidents_as_hints_and_v2_does_not()
    {
        InvestigateIncident command = Telemetry.ConnectionLeak().Command(70) with
        {
            SimilarIncidents = [new SimilarIncident(IncidentId.New(), 0.8, ["service:checkout-service"], "change:d0", "An earlier deployment leaked connections.", "resolved", Telemetry.Start)]
        };

        InvestigatorPrompt.Task(command, InvestigatorPrompt.V3).ShouldContain("similarIncidents");
        InvestigatorPrompt.System(new Dictionary<string, string>(StringComparer.Ordinal), Specialists.Generalist, InvestigatorPrompt.V3).ShouldContain("never evidence");
        InvestigatorPrompt.Task(command, InvestigatorPrompt.V2).ShouldNotContain("similarIncidents");
        InvestigatorPrompt.System(new Dictionary<string, string>(StringComparer.Ordinal), Specialists.Generalist, InvestigatorPrompt.V2).ShouldNotContain("similarIncidents");
    }

    private static async Task<AgentExecutionRecord> RecordAsync(Telemetry telemetry, InMemoryExecutionStore store, IModelProvider model, string role)
    {
        var capture = new InvestigationCapture();
        ModelInvestigator investigator = Investigator(new CapturingObservationQueries(telemetry, capture), new CapturingKnowledgeQueries(telemetry, capture), model, capture);
        InvestigateIncident command = telemetry.Command(70, role);
        capture.BeginRecording();
        InvestigationRun run = await investigator.InvestigateAsync(command, false, CancellationToken.None);
        await SaveRunAsync(store, run, command, capture.EndRecording());
        run.Execution.ToolCalls.ShouldNotBeEmpty();
        return run.Execution;
    }

    private static async Task SaveRunAsync(InMemoryExecutionStore store, InvestigationRun run, InvestigateIncident command, IReadOnlyList<CapturedResponse> responses)
    {
        await store.SaveAsync(run.Execution, CancellationToken.None);
        await store.SaveInputsAsync(new ExecutionInputs
        {
            AgentExecutionId = run.Execution.AgentExecutionId,
            Command = JsonSerializer.Serialize(command, SentinelJson.Options),
            Responses = responses,
            ToolCatalogVersion = ToolCatalog.Version,
            CapturedAt = Telemetry.Start
        }, CancellationToken.None);
    }

    private static ReplayInvestigationCommandHandler Replayer(InMemoryExecutionStore store, OfflineProviders offline, IModelProvider model)
    {
        var capture = new InvestigationCapture();
        var queries = new CapturingObservationQueries(offline, capture);
        var knowledge = new CapturingKnowledgeQueries(offline, capture);
        return new ReplayInvestigationCommandHandler(
            store,
            [Investigator(queries, knowledge, model, capture)],
            Tools(queries, knowledge),
            new FixedRouter(model),
            capture,
            TimeProvider.System,
            NullLogger<ReplayInvestigationCommandHandler>.Instance);
    }

    private static ModelInvestigator Investigator(IObservationQueries queries, IKnowledgeQueries knowledge, IModelProvider model, InvestigationCapture capture) =>
        new(
            new FixedRouter(model),
            Tools(queries, knowledge),
            Harness.Catalog,
            Options.Create(new ModelPricingOptions { InputPerMillionTokens = 1, OutputPerMillionTokens = 2 }),
            new AgentWorkerTelemetry(),
            TimeProvider.System,
            NullLogger<ModelInvestigator>.Instance,
            capture);

    private static IInvestigationTool[] Tools(IObservationQueries queries, IKnowledgeQueries knowledge) =>
    [
        new MetricRangeTool(queries, Harness.Catalog),
        new RecentDeploymentsTool(queries),
        new LogSignaturesTool(queries),
        new SlowSpansTool(queries),
        new ServiceDependenciesTool(queries),
        new RunbookSearchTool(knowledge)
    ];

    /// <summary>The metrics specialist's findings, turned into the verifier's command.</summary>
    private static async Task<InvestigateIncident> ChallengeCommandAsync(Telemetry telemetry)
    {
        InvestigationRun proposed = await Harness.Investigator(telemetry, Harness.Catalog, Harness.AllTools(telemetry)).InvestigateAsync(telemetry.Command(70, InvestigationRoles.Changes), false, CancellationToken.None);
        return telemetry.Command(70, InvestigationRoles.Verifier) with { Hypotheses = proposed.Completed.Hypotheses, Evidence = proposed.Completed.Evidence };
    }

    private sealed class FixedRouter(IModelProvider model) : IModelRouter
    {
        public IReadOnlyCollection<string> Configured => [model.Provider];

        public IModelProvider For(string role) => model;
    }

    /// <summary>Live providers that must never be reached during a replay.</summary>
    private sealed class OfflineProviders : IObservationQueries, IKnowledgeQueries
    {
        public int Calls { get; private set; }

        public Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("A replay reached a live observation provider.");
        }

        public Task<KnowledgeResult> SearchAsync(KnowledgeQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("A replay reached a live knowledge provider.");
        }
    }

    /// <summary>The test double's answers from a model that does not promise determinism, counting its calls.</summary>
    private sealed class NondeterministicModel(IModelProvider inner) : IModelProvider
    {
        public int Calls { get; private set; }

        public string Provider => "live-like";

        public string Model => inner.Model;

        public string? ModelVersion => inner.ModelVersion;

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return inner.CompleteAsync(request, cancellationToken);
        }
    }

    private sealed class InMemoryExecutionStore : IExecutionStore
    {
        private readonly Dictionary<AgentExecutionId, AgentExecutionRecord> _executions = [];
        private readonly Dictionary<AgentExecutionId, ExecutionInputs> _inputs = [];

        public List<InvestigationReplayResult> Replays { get; } = [];

        public void ForgetInputs(AgentExecutionId executionId) => _inputs.Remove(executionId);

        public Task SaveAsync(AgentExecutionRecord execution, CancellationToken cancellationToken)
        {
            _executions[execution.AgentExecutionId] = execution;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentExecutionRecord>> ForIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentExecutionRecord>>([.. _executions.Values.Where(execution => execution.IncidentId == incidentId)]);

        public Task<AgentExecutionRecord?> GetAsync(AgentExecutionId executionId, CancellationToken cancellationToken) =>
            Task.FromResult(_executions.GetValueOrDefault(executionId));

        public Task SaveInputsAsync(ExecutionInputs inputs, CancellationToken cancellationToken)
        {
            _inputs[inputs.AgentExecutionId] = inputs;
            return Task.CompletedTask;
        }

        public Task<ExecutionInputs?> GetInputsAsync(AgentExecutionId executionId, CancellationToken cancellationToken) =>
            Task.FromResult(_inputs.GetValueOrDefault(executionId));

        public Task SaveReplayAsync(InvestigationReplayResult replay, CancellationToken cancellationToken)
        {
            Replays.Add(replay);
            return Task.CompletedTask;
        }
    }
}
