using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>AgentWorker's own audit store for executions, their tool calls, their captured inputs and their replays (ADR-007, Phase 07).</summary>
public interface IExecutionStore
{
    Task SaveAsync(AgentExecution execution, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentExecution>> ForIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<AgentExecution?> GetAsync(AgentExecutionId executionId, CancellationToken cancellationToken);

    Task SaveInputsAsync(ExecutionInputs inputs, CancellationToken cancellationToken);

    Task<ExecutionInputs?> GetInputsAsync(AgentExecutionId executionId, CancellationToken cancellationToken);

    Task SaveReplayAsync(Sentinel.Contracts.Incidents.InvestigationReplayResult replay, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the configured investigator for an incident and sends its findings to Runtime. The
/// deploy-blame baseline runs in shadow alongside it and is recorded, never sent, so every
/// incident carries its own baseline comparison (Phase 01A, Phase 07).
/// </summary>
public sealed class InvestigationService(
    IEnumerable<IInvestigator> investigators,
    IExecutionStore executions,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    InvestigationCapture capture,
    TimeProvider time,
    IOptions<InvestigationOptions> options,
    ILogger<InvestigationService> logger)
{
    public async Task HandleAsync(InvestigateIncident command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        InvestigationOptions settings = options.Value;

        // The verifier challenges what the investigators proposed; it is never the configured investigator (Phase 05).
        IInvestigator primary = command.Role == InvestigationRoles.Verifier ? Find(AdversarialVerifier.InvestigatorName) : Find(settings.Investigator);
        InvestigationRun run = await RunAndRecordAsync(primary, command, shadow: false, cancellationToken);

        // One baseline per incident: alongside the generalist, or the changes specialist when specialists run.
        if (settings.ShadowBaseline && primary.Name != DeployBlameInvestigator.InvestigatorName && command.Role is null or InvestigationRoles.Generalist or InvestigationRoles.Changes)
        {
            await RunAndRecordAsync(Find(DeployBlameInvestigator.InvestigatorName), command, shadow: true, cancellationToken);
        }

        outbox.Enqueue(run.Completed, new MessageMetadata(
            CorrelationId: command.IncidentId.ToString(),
            Headers: SentinelHeaders.For(command.TenantId, command.EnvironmentId, command.AffectedServiceId, command.IncidentId)));
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Investigation {InvestigationId} by {Investigator} as {Role} ended {Outcome}: {Hypotheses} hypotheses, {Evidence} evidence, {ToolCalls} tool calls, {Tokens} tokens",
            command.InvestigationId,
            primary.Name,
            command.Role ?? InvestigationRoles.Generalist,
            run.Completed.Outcome,
            run.Completed.Hypotheses.Count,
            run.Completed.Evidence.Count,
            run.Execution.ToolCalls.Count,
            run.Execution.InputTokens + run.Execution.OutputTokens);
    }

    /// <summary>Runs one investigator with its provider responses captured, and records the execution with its inputs (Phase 07).</summary>
    private async Task<InvestigationRun> RunAndRecordAsync(IInvestigator investigator, InvestigateIncident command, bool shadow, CancellationToken cancellationToken)
    {
        capture.BeginRecording();
        InvestigationRun run;
        try
        {
            run = await investigator.InvestigateAsync(command, shadow, cancellationToken);
        }
        finally
        {
            capture.EndRecording();
        }

        await executions.SaveAsync(run.Execution, cancellationToken);
        await executions.SaveInputsAsync(new ExecutionInputs
        {
            AgentExecutionId = run.Execution.AgentExecutionId,
            Command = JsonSerializer.Serialize(command, SentinelJson.Options),
            Responses = capture.LastRecording,
            ToolCatalogVersion = ToolCatalog.Version,
            CapturedAt = time.GetUtcNow()
        }, cancellationToken);
        return run;
    }

    private IInvestigator Find(string name) =>
        investigators.FirstOrDefault(investigator => investigator.Name == name)
        ?? throw new InvalidOperationException($"No investigator named '{name}'. Configure Investigation:Investigator as model or {DeployBlameInvestigator.InvestigatorName}.");
}
