using System.Diagnostics;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Capture;
using Sentinel.AgentWorker.Application.Diagnostics;
using Sentinel.AgentWorker.Application.Options;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// The model-driven investigator for every role (Phase 04): a tool-call loop through the model the
/// router picks for the role, with only the role's tools. The harness, not the model, executes and
/// bounds tools and validates the output; the execution enforces the token and tool budgets and allows
/// invalid output one repair attempt, after which the run abstains.
/// </summary>
public sealed class ModelInvestigator(
    IModelRouter models,
    IEnumerable<IInvestigationTool> tools,
    IOptions<InvestigationOptions> options,
    IOptions<ModelPricingOptions> pricing,
    AgentWorkerTelemetry telemetry,
    TimeProvider time,
    ILogger<ModelInvestigator> logger,
    InvestigationCapture? capture = null,
    ModelSpendGuard? spend = null) : IInvestigator
{
    public static readonly ActivitySource Activities = new("Sentinel.AgentWorker.Investigations");

    private const string TruncatedAnswer = "Your answer was cut off at the output limit. Reply with the JSON document only, with every statement in one short sentence.";

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

        AgentExecution execution = InvestigationRecords.Start(
            command, Name, shadow, new ExecutionModel(model.Provider, model.Model, model.ModelVersion, InvestigatorPrompt.VersionFor(profile, promptVersion)), time.GetUtcNow());
        var state = new RunState(execution, profile);
        var permitted = _tools.Where(tool => profile.Tools.Contains(tool.Key)).ToDictionary(StringComparer.Ordinal);
        ModelToolDefinition[] definitions = [.. permitted.Values.Select(tool => tool.Definition)];
        var context = new ToolContext(command.TenantId, command.EnvironmentId, command.IncidentId, command.AffectedServiceId, command.RelatedServiceIds, command.ObservationWindow);
        var messages = new List<ModelMessage>
        {
            ModelMessage.System(InvestigatorPrompt.System(new Dictionary<string, string>(settings.Metrics, StringComparer.Ordinal), profile, promptVersion)),
            ModelMessage.User(InvestigatorPrompt.Task(command, promptVersion))
        };

        ModelPricingOptions fallback = pricing.Value;
        ModelPrice price = model.Price ?? new ModelPrice(fallback.InputPerMillionTokens, fallback.InputPerMillionTokens, fallback.OutputPerMillionTokens);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(command.Budget.Timeout);
        try
        {
            for (int turn = 0; turn < settings.MaxModelTurns; turn++)
            {
                if (spend is not null && await spend.RefusalAsync(model, price, timeout.Token) is { } refusal)
                {
                    return Finish(command, state, InvestigationOutcome.Abstained, refusal);
                }

                long called = Stopwatch.GetTimestamp();
                ModelResponse response = await model.CompleteAsync(
                    new ModelRequest(InvestigatorPrompt.VersionFor(profile, promptVersion), messages, definitions, FindingsValidator.OutputSchemaName, settings.MaxOutputTokens, FindingsValidator.OutputJsonSchema),
                    timeout.Token);
                double cost = price.Estimate(response.Usage);
                telemetry.ModelCall(model, profile.Role, response, Stopwatch.GetElapsedTime(called), cost);
                Result budget = execution.RecordModelCall(response.Usage.InputTokens, response.Usage.OutputTokens, command.Budget.MaxModelTokens, cost);
                if (budget.IsFailure)
                {
                    return Finish(command, state, InvestigationOutcome.Abstained, budget.Error.Description);
                }

                switch (response.Finish)
                {
                    case ModelFinish.ToolCalls:
                        messages.Add(ModelMessage.Assistant(response.ToolCalls));
                        foreach (ModelToolCall call in response.ToolCalls)
                        {
                            string result = await RunToolAsync(call, permitted, context, command.Budget.MaxToolCalls, state, timeout.Token);
                            messages.Add(ModelMessage.ToolResult(call.Id, InvestigatorPrompt.ToolResult(call.Id, result, promptVersion)));
                        }

                        break;
                    case ModelFinish.Final:
                        ValidatedFindings findings = FindingsValidator.Validate(response.Content, state.Returned, state.ToolCallIds, profile.MinimumHypotheses);
                        if (findings.Abstained)
                        {
                            return Finish(command, state, InvestigationOutcome.Abstained, findings.AbstentionReason);
                        }

                        if (findings.IsValid)
                        {
                            state.Evidence = [.. findings.Evidence.Select(evidence => evidence with { Partial = evidence.SourceObservationIds.Any(state.PartialObservations.Contains) })];
                            state.Hypotheses = findings.Hypotheses;
                            state.Unknowns = findings.Unknowns ?? [];
                            return Finish(command, state, InvestigationOutcome.Completed, null);
                        }

                        Result repair = execution.RecordSchemaFailure();
                        telemetry.SchemaFailure(model, profile.Role);
                        logger.LogWarning("Investigation {InvestigationId}: structured output rejected ({Count} problems)", command.InvestigationId, findings.Errors.Count);
                        if (repair.IsFailure)
                        {
                            return Finish(command, state, InvestigationOutcome.Abstained, $"{repair.Error.Description}: {string.Join(" ", findings.Errors.Take(5))}");
                        }

                        messages.Add(ModelMessage.User(InvestigatorPrompt.Repair(findings.Errors)));
                        break;
                    case ModelFinish.Truncated:
                        // Reasoning counts against the output limit, so a cut-off answer gets the same single repair as an invalid one.
                        telemetry.SchemaFailure(model, profile.Role);
                        if (execution.RecordSchemaFailure() is { IsFailure: true } exhausted)
                        {
                            return Finish(command, state, InvestigationOutcome.Abstained, $"{exhausted.Error.Description}: {response.Error}");
                        }

                        messages.Add(ModelMessage.User(InvestigatorPrompt.Repair([TruncatedAnswer])));
                        break;
                    case ModelFinish.Refused:
                        return Finish(command, state, InvestigationOutcome.Abstained, $"The model refused: {response.Error}");
                    default:
                        return Finish(command, state, InvestigationOutcome.Failed, $"The model call ended with {response.Finish}: {response.Error}");
                }
            }

            return Finish(command, state, InvestigationOutcome.Abstained, $"No conclusion within {settings.MaxModelTurns} model turns.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Finish(command, state, InvestigationOutcome.Abstained, $"The investigation exceeded its {command.Budget.Timeout} time budget.");
        }
    }

    private async Task<string> RunToolAsync(ModelToolCall call, Dictionary<string, IInvestigationTool> permitted, ToolContext context, int maxToolCalls, RunState state, CancellationToken cancellationToken)
    {
        DateTimeOffset started = time.GetUtcNow();
        ToolOutcome outcome;
        if (!state.Execution.HasToolBudget(maxToolCalls))
        {
            outcome = ToolOutcome.Failure(AgentExecutionErrors.ToolBudgetExhausted(maxToolCalls).Description);
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
        state.Execution.RecordToolCall(new ToolCallRecord
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

    private InvestigationRun Finish(InvestigateIncident command, RunState state, InvestigationOutcome outcome, string? reason)
    {
        InvestigationRecords.Finish(state.Execution, outcome, reason, time.GetUtcNow());
        InvestigationRun run = InvestigationRecords.Build(command, state.Execution, state.Evidence, state.Hypotheses);
        return run.WithCompleted(run.Completed with { Unknowns = state.Unknowns.Count > 0 ? state.Unknowns : null });
    }

    // What the harness needs while the run goes on; the execution keeps what the run spends and records.
    private sealed class RunState(AgentExecution execution, SpecialistProfile profile)
    {
        public AgentExecution Execution { get; } = execution;

        public SpecialistProfile Profile { get; } = profile;

        public IReadOnlyList<string> Unknowns { get; set; } = [];

        public HashSet<ObservationId> Returned { get; } = [];

        public HashSet<ObservationId> PartialObservations { get; } = [];

        public HashSet<string> ToolCallIds { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<EvidenceFinding> Evidence { get; set; } = [];

        public IReadOnlyList<HypothesisFinding> Hypotheses { get; set; } = [];
    }
}
