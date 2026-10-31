using System.Text.Json;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Diagnostics;
using Sentinel.AgentWorker.Application.Options;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Investigations;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// The escalation investigator (Phase E05, ADR-024 role 1): when consensus abstained because two explanations stood too
/// close, the model the router picks for the role proposes one discriminating query over structured state. AgentWorker
/// validates it, runs it through the catalogue's read-only tool inside the incident window, and turns the predictions that
/// held into evidence. The model never sees the result, proposes no diagnosis and runs once.
/// </summary>
public sealed class EscalationInvestigator(
    IModelRouter models,
    IEnumerable<IInvestigationTool> tools,
    IOptions<InvestigationOptions> options,
    IOptions<ModelPricingOptions> pricing,
    AgentWorkerTelemetry telemetry,
    TimeProvider time,
    ModelSpendGuard? spend = null) : IInvestigator
{
    public const string InvestigatorName = "escalation";

    private const string ToolCallId = "call_escalation_query";

    public string Name => InvestigatorName;

    public async Task<InvestigationRun> InvestigateAsync(InvestigateIncident command, bool shadow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        IModelProvider model = models.For(InvestigationRoles.Escalation);
        AgentExecution execution = InvestigationRecords.Start(command, Name, shadow, new ExecutionModel(model.Provider, model.Model, model.ModelVersion, EscalationPrompt.Version), time.GetUtcNow());
        var metrics = new Dictionary<string, string>(options.Value.Metrics, StringComparer.Ordinal);
        ModelPricingOptions fallback = pricing.Value;
        ModelPrice price = model.Price ?? new ModelPrice(fallback.InputPerMillionTokens, fallback.InputPerMillionTokens, fallback.OutputPerMillionTokens);
        if (spend is not null && await spend.RefusalAsync(model, price, cancellationToken) is { } refusal)
        {
            return Finish(command, execution, InvestigationOutcome.Abstained, refusal, [], null, null);
        }

        var messages = new List<ModelMessage> { ModelMessage.System(EscalationPrompt.System(metrics)), ModelMessage.User(EscalationPrompt.Task(command)) };
        DiscriminatingQuery? query = null;
        IReadOnlyList<string> errors = [];

        // One proposal and, if it is refused, one repair: the role is bounded like every investigator.
        for (int attempt = 0; attempt < 2 && query is null; attempt++)
        {
            ModelResponse response = await model.CompleteAsync(
                new ModelRequest(EscalationPrompt.Version, messages, [], EscalationPrompt.OutputSchemaName, options.Value.MaxOutputTokens, EscalationPrompt.OutputJsonSchema),
                cancellationToken);
            double cost = price.Estimate(response.Usage);
            telemetry.ModelCall(model, InvestigationRoles.Escalation, response, TimeSpan.Zero, cost);
            if (execution.RecordModelCall(response.Usage.InputTokens, response.Usage.OutputTokens, command.Budget.MaxModelTokens, cost) is { IsFailure: true } budget)
            {
                return Finish(command, execution, InvestigationOutcome.Abstained, budget.Error.Description, [], null, null);
            }

            if (response.Finish != ModelFinish.Final)
            {
                return Finish(command, execution, InvestigationOutcome.Abstained, $"The escalation model call ended with {response.Finish}: {response.Error}", [], null, null);
            }

            (query, errors) = DiscriminatingQueries.Parse(response.Content ?? string.Empty, command, metrics);
            if (query is null)
            {
                _ = execution.RecordSchemaFailure();
                telemetry.SchemaFailure(model, InvestigationRoles.Escalation);
                messages.Add(ModelMessage.User(InvestigatorPrompt.Repair(errors)));
            }
        }

        if (query is null)
        {
            return Finish(command, execution, InvestigationOutcome.Abstained, $"The proposed query was refused: {string.Join(" ", errors.Take(5))}", [], null, null);
        }

        // The harness, not the model, runs the query: the catalogue's read-only tool, bounded to the incident window.
        IInvestigationTool tool = tools.Single(candidate => candidate.Definition.Name == query.Tool);
        var context = new ToolContext(command.TenantId, command.EnvironmentId, command.IncidentId, command.AffectedServiceId, command.RelatedServiceIds, command.ObservationWindow);
        DateTimeOffset started = time.GetUtcNow();
        ToolOutcome outcome;
        using (var arguments = JsonDocument.Parse(query.Arguments))
        {
            try
            {
                outcome = await tool.InvokeAsync(context, arguments.RootElement, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                outcome = ToolOutcome.Failure($"The observation query failed: {exception.Message}");
            }
        }

        execution.RecordToolCall(new ToolCallRecord
        {
            ToolCallId = ToolCallId,
            ToolName = query.Tool,
            Arguments = query.Arguments,
            Result = outcome.Content,
            ObservationIds = outcome.ObservationIds,
            Error = outcome.Error,
            StartedAt = started,
            Duration = time.GetUtcNow() - started
        });
        if (outcome.Error is not null)
        {
            return Finish(command, execution, InvestigationOutcome.Abstained, $"The discriminating query could not run: {outcome.Error}", [], null, query);
        }

        (IReadOnlyList<EvidenceFinding> evidence, string? unknown) = DiscriminatingQueries.Measure(query, outcome.Content, ToolCallId, command.SymptomOnset ?? command.ObservationWindow.To);
        return Finish(command, execution, InvestigationOutcome.Completed, null, [.. evidence.Select(item => item with { Partial = outcome.Partial })], unknown, query);
    }

    private InvestigationRun Finish(InvestigateIncident command, AgentExecution execution, InvestigationOutcome outcome, string? reason, IReadOnlyList<EvidenceFinding> evidence, string? unknown, DiscriminatingQuery? query)
    {
        InvestigationRecords.Finish(execution, outcome, reason, time.GetUtcNow());
        InvestigationRun run = InvestigationRecords.Build(command, execution, evidence, []);
        return run.WithCompleted(run.Completed with
        {
            Role = InvestigationRoles.Escalation,
            Query = query,
            Unknowns = unknown is null ? run.Completed.Unknowns : [unknown]
        });
    }
}
