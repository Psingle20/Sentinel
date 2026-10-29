using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Abstractions.Persistence;
using Sentinel.AgentWorker.Application.Capture;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Options;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Commands.Investigations;

/// <summary>
/// Runs the configured investigator for an incident and sends its findings to Runtime. The
/// deploy-blame baseline runs in shadow alongside it and is recorded, never sent, so every
/// incident carries its own baseline comparison (Phase 01A, Phase 07).
/// </summary>
public sealed class InvestigateIncidentCommandHandler(
    IEnumerable<IInvestigator> investigators,
    IExecutionStore executions,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    InvestigationCapture capture,
    TimeProvider time,
    IOptions<InvestigationOptions> options,
    ILogger<InvestigateIncidentCommandHandler> logger) : ICommandHandler<InvestigateIncidentCommand>
{
    public async Task<Result> Handle(InvestigateIncidentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        InvestigateIncident message = command.Message;
        InvestigationOptions settings = options.Value;

        // The verifier challenges what the investigators proposed; it is never the configured investigator (Phase 05).
        IInvestigator primary = message.Role == InvestigationRoles.Verifier ? Find(AdversarialVerifier.InvestigatorName) : Find(settings.Investigator);
        InvestigationRun run = await RunAndRecordAsync(primary, message, shadow: false, cancellationToken);

        // One baseline per incident: alongside the generalist, or the changes specialist when specialists run.
        if (settings.ShadowBaseline && primary.Name != DeployBlameInvestigator.InvestigatorName && message.Role is null or InvestigationRoles.Generalist or InvestigationRoles.Changes)
        {
            await RunAndRecordAsync(Find(DeployBlameInvestigator.InvestigatorName), message, shadow: true, cancellationToken);
        }

        outbox.Enqueue(run.Completed, new MessageMetadata(
            CorrelationId: message.IncidentId.ToString(),
            Headers: SentinelHeaders.For(message.TenantId, message.EnvironmentId, message.AffectedServiceId, message.IncidentId)));
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Investigation {InvestigationId} by {Investigator} as {Role} ended {Outcome}: {Hypotheses} hypotheses, {Evidence} evidence, {ToolCalls} tool calls, {Tokens} tokens",
            message.InvestigationId,
            primary.Name,
            message.Role ?? InvestigationRoles.Generalist,
            run.Completed.Outcome,
            run.Completed.Hypotheses.Count,
            run.Completed.Evidence.Count,
            run.Execution.ToolCalls.Count,
            run.Execution.InputTokens + run.Execution.OutputTokens);
        return Result.Success();
    }

    /// <summary>Runs one investigator with its provider responses captured, and records the execution with its inputs (Phase 07).</summary>
    private async Task<InvestigationRun> RunAndRecordAsync(IInvestigator investigator, InvestigateIncident message, bool shadow, CancellationToken cancellationToken)
    {
        capture.BeginRecording();
        InvestigationRun run;
        try
        {
            run = await investigator.InvestigateAsync(message, shadow, cancellationToken);
        }
        finally
        {
            capture.EndRecording();
        }

        await executions.SaveAsync(run.Execution, cancellationToken);
        await executions.SaveInputsAsync(new ExecutionInputs
        {
            AgentExecutionId = run.Execution.AgentExecutionId,
            Command = JsonSerializer.Serialize(message, SentinelJson.Options),
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
