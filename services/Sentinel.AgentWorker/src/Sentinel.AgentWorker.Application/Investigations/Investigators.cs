using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Models;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>One investigator run: the message for Runtime and the audit record for AgentWorker.</summary>
public sealed record InvestigationRun(InvestigationCompleted Completed, AgentExecution Execution)
{
    /// <summary>Replaces the findings and keeps the audit record's copy identical to what is sent (Phase 07 replay compares against it).</summary>
    public InvestigationRun WithCompleted(InvestigationCompleted completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        return new InvestigationRun(completed, Execution with { Findings = JsonSerializer.Serialize(completed, SentinelJson.Options) });
    }
}

/// <summary>
/// The investigator contract (Phase 01A): every investigator, model-driven or not, answers the same
/// command with the same findings contract, so they can be swapped and compared.
/// </summary>
public interface IInvestigator
{
    string Name { get; }

    Task<InvestigationRun> InvestigateAsync(InvestigateIncident command, bool shadow, CancellationToken cancellationToken);
}

/// <summary>
/// The model-driven investigator for every role (Phase 04): a tool-call loop through the model the
/// router picks for the role, with only the role's tools. The harness, not the model, executes and
/// bounds tools, enforces the budget and validates the output; invalid output gets one repair
/// attempt and otherwise leads to abstention.
/// </summary>
public sealed class ModelInvestigator(
    IModelRouter models,
    IEnumerable<IInvestigationTool> tools,
    IOptions<InvestigationOptions> options,
    IOptions<ModelPricingOptions> pricing,
    AgentWorkerTelemetry telemetry,
    TimeProvider time,
    ILogger<ModelInvestigator> logger,
    InvestigationCapture? capture = null) : IInvestigator
{
    public static readonly ActivitySource Activities = new("Sentinel.AgentWorker.Investigations");

    private readonly Dictionary<string, IInvestigationTool> _tools = tools.ToDictionary(tool => tool.Definition.Name, StringComparer.Ordinal);

    public string Name => "model";

    public async Task<InvestigationRun> InvestigateAsync(InvestigateIncident command, bool shadow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        SpecialistProfile profile = Specialists.For(command.Role);
        IModelProvider model = models.For(profile.Role);
        using Activity? activity = Activities.StartActivity("investigate", ActivityKind.Internal);
        activity?.SetTag("sentinel.incident_id", command.IncidentId.ToString());
        activity?.SetTag("sentinel.role", profile.Role);
        activity?.SetTag("gen_ai.request.model", model.Model);

        InvestigationOptions settings = options.Value;
        string promptVersion = capture?.PromptVersion ?? settings.PromptVersion;
        if (!InvestigatorPrompt.IsKnown(promptVersion))
        {
            throw new InvalidOperationException($"Prompt version '{promptVersion}' is not one of {string.Join(", ", InvestigatorPrompt.Versions)}.");
        }

        var state = new RunState(time.GetUtcNow(), profile, model, promptVersion);
        var permitted = _tools.Where(tool => profile.Tools.Contains(tool.Key)).ToDictionary(StringComparer.Ordinal);
        ModelToolDefinition[] definitions = [.. permitted.Values.Select(tool => tool.Definition)];
        var context = new ToolContext(command.TenantId, command.EnvironmentId, command.IncidentId, command.AffectedServiceId, command.RelatedServiceIds, command.ObservationWindow);
        var messages = new List<ModelMessage>
        {
            ModelMessage.System(InvestigatorPrompt.System(new Dictionary<string, string>(settings.Metrics, StringComparer.Ordinal), profile, promptVersion)),
            ModelMessage.User(InvestigatorPrompt.Task(command, promptVersion))
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(command.Budget.Timeout);
        try
        {
            for (int turn = 0; turn < settings.MaxModelTurns; turn++)
            {
                long called = Stopwatch.GetTimestamp();
                ModelResponse response = await model.CompleteAsync(new ModelRequest(InvestigatorPrompt.VersionFor(profile, promptVersion), messages, definitions, FindingsValidator.OutputSchemaName, settings.MaxOutputTokens), timeout.Token);
                telemetry.ModelCall(model, profile.Role, response, Stopwatch.GetElapsedTime(called), pricing.Value.Estimate(response.Usage));
                state.ModelCalls++;
                state.InputTokens += response.Usage.InputTokens;
                state.OutputTokens += response.Usage.OutputTokens;
                if (state.InputTokens + state.OutputTokens > command.Budget.MaxModelTokens)
                {
                    return Finish(command, shadow, state, InvestigationOutcome.Abstained, "The model token budget was exhausted.");
                }

                switch (response.Finish)
                {
                    case ModelFinish.ToolCalls:
                        messages.Add(ModelMessage.Assistant(response.ToolCalls));
                        foreach (ModelToolCall call in response.ToolCalls)
                        {
                            messages.Add(ModelMessage.ToolResult(call.Id, await RunToolAsync(call, permitted, context, command.Budget.MaxToolCalls, state, timeout.Token)));
                        }

                        break;
                    case ModelFinish.Final:
                        ValidatedFindings findings = FindingsValidator.Validate(response.Content, state.Returned, state.ToolCallIds, profile.MinimumHypotheses);
                        if (findings.Abstained)
                        {
                            return Finish(command, shadow, state, InvestigationOutcome.Abstained, findings.AbstentionReason);
                        }

                        if (findings.IsValid)
                        {
                            state.Evidence = [.. findings.Evidence.Select(evidence => evidence with { Partial = evidence.SourceObservationIds.Any(state.PartialObservations.Contains) })];
                            state.Hypotheses = findings.Hypotheses;
                            state.Unknowns = findings.Unknowns ?? [];
                            return Finish(command, shadow, state, InvestigationOutcome.Completed, null);
                        }

                        state.SchemaFailures++;
                        telemetry.SchemaFailure(model, profile.Role);
                        logger.LogWarning("Investigation {InvestigationId}: structured output rejected ({Count} problems)", command.InvestigationId, findings.Errors.Count);
                        if (state.SchemaFailures > 1)
                        {
                            return Finish(command, shadow, state, InvestigationOutcome.Abstained, $"The model's output was invalid after a repair attempt: {string.Join(" ", findings.Errors.Take(5))}");
                        }

                        messages.Add(ModelMessage.User(InvestigatorPrompt.Repair(findings.Errors)));
                        break;
                    case ModelFinish.Refused:
                        return Finish(command, shadow, state, InvestigationOutcome.Abstained, $"The model refused: {response.Error}");
                    default:
                        return Finish(command, shadow, state, InvestigationOutcome.Failed, $"The model call ended with {response.Finish}: {response.Error}");
                }
            }

            return Finish(command, shadow, state, InvestigationOutcome.Abstained, $"No conclusion within {settings.MaxModelTurns} model turns.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Finish(command, shadow, state, InvestigationOutcome.Abstained, $"The investigation exceeded its {command.Budget.Timeout} time budget.");
        }
    }

    private async Task<string> RunToolAsync(ModelToolCall call, Dictionary<string, IInvestigationTool> permitted, ToolContext context, int maxToolCalls, RunState state, CancellationToken cancellationToken)
    {
        DateTimeOffset started = time.GetUtcNow();
        ToolOutcome outcome;
        if (state.ToolCalls.Count >= maxToolCalls)
        {
            outcome = ToolOutcome.Failure($"The tool budget of {maxToolCalls} calls is exhausted; conclude from what you have.");
        }
        else if (!permitted.TryGetValue(call.Name, out IInvestigationTool? tool))
        {
            // A tool outside the role is refused like an unknown one: roles bound what a model may touch.
            outcome = ToolOutcome.Failure($"Tool '{call.Name}' is not available to the {state.Profile.Role} investigator. Available: {string.Join(", ", permitted.Keys)}.");
        }
        else
        {
            try
            {
                using var arguments = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
                outcome = await tool.InvokeAsync(context, arguments.RootElement, cancellationToken);
            }
            catch (JsonException exception)
            {
                outcome = ToolOutcome.Failure($"Arguments are not valid JSON: {exception.Message}");
            }
            catch (HttpRequestException exception)
            {
                // A provider outage is an explicit tool error, never empty evidence (ADR-007).
                outcome = ToolOutcome.Failure($"The observation query failed: {exception.Message}");
            }
        }

        state.Returned.UnionWith(outcome.ObservationIds);
        state.ToolCallIds.Add(call.Id);
        if (outcome.Partial)
        {
            state.PartialObservations.UnionWith(outcome.ObservationIds);
        }

        telemetry.ToolCall(call.Name, time.GetUtcNow() - started, outcome.Error is not null);
        state.ToolCalls.Add(new ToolCallRecord
        {
            ToolCallId = call.Id,
            ToolName = call.Name,
            Arguments = call.Arguments,
            Result = outcome.Content,
            ObservationIds = outcome.ObservationIds,
            Partial = outcome.Partial,
            Error = outcome.Error,
            StartedAt = started,
            Duration = time.GetUtcNow() - started
        });
        return outcome.Content;
    }

    private InvestigationRun Finish(InvestigateIncident command, bool shadow, RunState state, InvestigationOutcome outcome, string? reason)
    {
        var metadata = new ModelMetadata(state.Model.Provider, state.Model.Model, state.Model.ModelVersion, InvestigatorPrompt.VersionFor(state.Profile, state.PromptVersion));
        InvestigationRun run = InvestigationRecords.Build(command, Name, shadow, metadata, outcome, reason, state.Evidence, state.Hypotheses, state.ToolCalls, state.ModelCalls, state.InputTokens, state.OutputTokens, state.SchemaFailures, state.StartedAt, time.GetUtcNow());
        return run.WithCompleted(run.Completed with { Unknowns = state.Unknowns.Count > 0 ? state.Unknowns : null });
    }

    private sealed class RunState(DateTimeOffset startedAt, SpecialistProfile profile, IModelProvider model, string promptVersion)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;

        public string PromptVersion { get; } = promptVersion;

        public SpecialistProfile Profile { get; } = profile;

        public IModelProvider Model { get; } = model;

        public IReadOnlyList<string> Unknowns { get; set; } = [];

        public HashSet<ObservationId> Returned { get; } = [];

        public HashSet<ObservationId> PartialObservations { get; } = [];

        public HashSet<string> ToolCallIds { get; } = new(StringComparer.Ordinal);

        public List<ToolCallRecord> ToolCalls { get; } = [];

        public IReadOnlyList<EvidenceFinding> Evidence { get; set; } = [];

        public IReadOnlyList<HypothesisFinding> Hypotheses { get; set; } = [];

        public int ModelCalls { get; set; }

        public int InputTokens { get; set; }

        public int OutputTokens { get; set; }

        public int SchemaFailures { get; set; }
    }
}

/// <summary>
/// The deploy-blame baseline (Phase 01, reference-system/scenarios/baseline/deploy-blame.md): blame
/// the latest deployment to the affected service or its dependencies inside the lookback window,
/// or abstain. It reads deployments only, so it cannot tell a cause from a coincidence.
/// </summary>
public sealed class DeployBlameInvestigator(IObservationQueries observations, TimeProvider time) : IInvestigator
{
    public const string InvestigatorName = "deploy-blame";
    public static readonly TimeSpan Lookback = TimeSpan.FromMinutes(10);
    private static readonly ModelMetadata Metadata = new("sentinel", InvestigatorName, "1", "deploy-blame-v1");

    public string Name => InvestigatorName;

    public async Task<InvestigationRun> InvestigateAsync(InvestigateIncident command, bool shadow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset started = time.GetUtcNow();
        DateTimeOffset detectedAt = command.ObservationWindow.To;
        var range = new TimeRange(detectedAt - Lookback, detectedAt);
        ObservationQueryResult result = await observations.QueryAsync(new ObservationQuery
        {
            TenantId = command.TenantId,
            EnvironmentId = command.EnvironmentId,
            Kind = ObservationKind.Deployment,
            Range = range,
            Limit = 500
        }, cancellationToken);

        HashSet<ServiceId> scope = [command.AffectedServiceId, .. command.RelatedServiceIds];
        DeploymentObservation[] deployments = [.. result.Observations.OfType<DeploymentObservation>()
            .Where(deployment => scope.Contains(deployment.ServiceId) && deployment.ChangeKind is "deploy" or "rollback")];
        var toolCall = new ToolCallRecord
        {
            ToolCallId = "baseline-deployments",
            ToolName = RecentDeploymentsTool.Name,
            Arguments = JsonSerializer.Serialize(new { services = scope, from = range.From, to = range.To }, SentinelJson.Options),
            Result = JsonSerializer.Serialize(deployments.Select(deployment => deployment.ObservationId), SentinelJson.Options),
            ObservationIds = [.. deployments.Select(deployment => deployment.ObservationId)],
            StartedAt = started,
            Duration = time.GetUtcNow() - started
        };

        DeploymentObservation? blamed = deployments
            .GroupBy(deployment => deployment.DeploymentId)
            .Select(group => group.OrderBy(deployment => deployment.EventTime).First())
            .OrderByDescending(deployment => deployment.StartedAt)
            .ThenBy(deployment => deployment.ServiceId.Value, StringComparer.Ordinal)
            .FirstOrDefault();

        if (blamed is null)
        {
            return InvestigationRecords.Build(command, Name, shadow, Metadata, InvestigationOutcome.Abstained, "No deployment to the service or its dependencies in the lookback window.", [], [], [toolCall], 0, 0, 0, 0, started, time.GetUtcNow());
        }

        ObservationId[] cited = [.. deployments.Where(deployment => deployment.DeploymentId == blamed.DeploymentId).Select(deployment => deployment.ObservationId)];
        var blame = HypothesisId.New();
        var recency = EvidenceId.New();
        EvidenceFinding evidence = new()
        {
            EvidenceId = recency,
            Statement = $"{blamed.ServiceId} {blamed.Version} is the most recent deployment before detection, started at {blamed.StartedAt:O}.",
            Direction = EvidenceDirection.Supports,
            Strength = 0.6,
            SourceObservationIds = cited,
            HypothesisIds = [blame],
            EventTimeRange = new TimeRange(blamed.StartedAt, blamed.CompletedAt ?? blamed.StartedAt),
            ToolCallId = toolCall.ToolCallId,
            IndependenceGroup = $"deployment:{blamed.DeploymentId}"
        };
        HypothesisFinding[] hypotheses =
        [
            new()
            {
                HypothesisId = blame,
                Statement = $"The {blamed.ServiceId} {blamed.Version} deployment caused the symptoms.",
                CausalChain = [$"deploy {blamed.ServiceId} {blamed.Version}", "regression", "SLO burn"],
                SuspectedServiceId = blamed.ServiceId,
                SuspectedChangeObservationId = blamed.ObservationId,
                SupportingEvidenceIds = [recency]
            },
            new()
            {
                HypothesisId = HypothesisId.New(),
                Statement = "The symptoms have a cause other than a deployment.",
                CausalChain = ["unknown cause", "SLO burn"]
            }
        ];

        return InvestigationRecords.Build(command, Name, shadow, Metadata, InvestigationOutcome.Completed, null, [evidence], hypotheses, [toolCall], 0, 0, 0, 0, started, time.GetUtcNow());
    }
}

internal static class InvestigationRecords
{
    public static InvestigationRun Build(
        InvestigateIncident command,
        string investigator,
        bool shadow,
        ModelMetadata model,
        InvestigationOutcome outcome,
        string? reason,
        IReadOnlyList<EvidenceFinding> evidence,
        IReadOnlyList<HypothesisFinding> hypotheses,
        IReadOnlyList<ToolCallRecord> toolCalls,
        int modelCalls,
        int inputTokens,
        int outputTokens,
        int schemaFailures,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        var executionId = AgentExecutionId.New();
        bool completed = outcome == InvestigationOutcome.Completed;
        var message = new InvestigationCompleted
        {
            TenantId = command.TenantId,
            EnvironmentId = command.EnvironmentId,
            IncidentId = command.IncidentId,
            InvestigationId = command.InvestigationId,
            AgentExecutionId = executionId,
            Outcome = outcome,
            Evidence = completed ? evidence : [],
            Hypotheses = completed ? hypotheses : [],
            AbstentionReason = reason,
            Model = model,
            CompletedAt = completedAt,
            Role = command.Role,
            ToolCatalogVersion = ToolCatalog.Version
        };

        var execution = new AgentExecution
        {
            AgentExecutionId = executionId,
            TenantId = command.TenantId,
            EnvironmentId = command.EnvironmentId,
            IncidentId = command.IncidentId,
            InvestigationId = command.InvestigationId,
            Investigator = investigator,
            Shadow = shadow,
            ModelProvider = model.Provider,
            Model = model.Model,
            ModelVersion = model.ModelVersion,
            PromptVersion = model.PromptVersion,
            Result = outcome switch
            {
                InvestigationOutcome.Completed => ExecutionResult.Completed,
                InvestigationOutcome.Abstained => ExecutionResult.Abstained,
                _ => ExecutionResult.Failed
            },
            AbstentionReason = reason,
            ToolCalls = toolCalls,
            ModelCalls = modelCalls,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            SchemaFailures = schemaFailures,
            Findings = JsonSerializer.Serialize(message, SentinelJson.Options),
            StartedAt = startedAt,
            CompletedAt = completedAt
        };

        return new InvestigationRun(message, execution);
    }
}
