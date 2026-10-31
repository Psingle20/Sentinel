using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>Ends a run and turns it into the message for Runtime and the audit record AgentWorker keeps.</summary>
internal static class InvestigationRecords
{
    /// <summary>Starts the execution of <paramref name="investigator"/> answering <paramref name="command"/>.</summary>
    public static AgentExecution Start(InvestigateIncident command, string investigator, bool shadow, ExecutionModel model, DateTimeOffset startedAt) =>
        AgentExecution.Start(AgentExecutionId.New(), command.TenantId, command.EnvironmentId, command.IncidentId, command.InvestigationId, investigator, shadow, model, startedAt);

    /// <summary>Finishes a running execution with <paramref name="outcome"/>; a finished one cannot finish again.</summary>
    public static void Finish(AgentExecution execution, InvestigationOutcome outcome, string? reason, DateTimeOffset completedAt)
    {
        Result finished = outcome switch
        {
            InvestigationOutcome.Completed => execution.Complete(completedAt),
            InvestigationOutcome.Abstained => execution.Abstain(reason ?? string.Empty, completedAt),
            _ => execution.Fail(reason ?? string.Empty, completedAt)
        };
        if (finished.IsFailure)
        {
            throw new InvalidOperationException($"Execution {execution.AgentExecutionId} could not finish: {finished.Error.Description}");
        }
    }

    /// <summary>The findings of a finished execution as Runtime receives them, and its audit record with the same findings.</summary>
    public static InvestigationRun Build(
        InvestigateIncident command,
        AgentExecution execution,
        IReadOnlyList<EvidenceFinding> evidence,
        IReadOnlyList<HypothesisFinding> hypotheses)
    {
        InvestigationOutcome outcome = execution.State switch
        {
            ExecutionState.Completed => InvestigationOutcome.Completed,
            ExecutionState.Abstained => InvestigationOutcome.Abstained,
            ExecutionState.Failed => InvestigationOutcome.Failed,
            _ => throw new InvalidOperationException($"Execution {execution.AgentExecutionId} is still running.")
        };
        bool completed = outcome == InvestigationOutcome.Completed;
        var message = new InvestigationCompleted
        {
            TenantId = command.TenantId,
            EnvironmentId = command.EnvironmentId,
            IncidentId = command.IncidentId,
            InvestigationId = command.InvestigationId,
            AgentExecutionId = execution.AgentExecutionId,
            Outcome = outcome,
            Evidence = completed ? evidence : [],
            Hypotheses = completed ? hypotheses : [],
            AbstentionReason = execution.AbstentionReason,
            Model = new ModelMetadata(execution.Model.Provider, execution.Model.Model, execution.Model.ModelVersion, execution.Model.PromptVersion),
            CompletedAt = execution.CompletedAt!.Value,
            Role = command.Role,
            ToolCatalogVersion = ToolCatalog.Version,
            // Reported whatever the outcome: an attempt that made an investigator abstain is worth seeing most of all.
            SuspectedInjections = UntrustedInstructions.Scan(execution.ToolCalls) is { Count: > 0 } suspected ? suspected : null
        };

        return new InvestigationRun(message, execution.ToRecord(JsonSerializer.Serialize(message, SentinelJson.Options)));
    }
}
