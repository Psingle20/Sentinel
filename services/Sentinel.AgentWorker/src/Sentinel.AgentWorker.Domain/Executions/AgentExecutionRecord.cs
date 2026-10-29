using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Domain.Executions;

/// <summary>
/// The audit record of one investigator run: who ran, with which model and prompt, which tools it
/// called, what it cost and how it ended. A shadow execution ran alongside the primary one for
/// comparison and was never sent to Runtime.
/// </summary>
public sealed record AgentExecutionRecord
{
    public required AgentExecutionId AgentExecutionId { get; init; }

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required InvestigationId InvestigationId { get; init; }

    public required string Investigator { get; init; }

    public required bool Shadow { get; init; }

    public required string ModelProvider { get; init; }

    public required string Model { get; init; }

    public string? ModelVersion { get; init; }

    public required string PromptVersion { get; init; }

    public required ExecutionResult Result { get; init; }

    public string? AbstentionReason { get; init; }

    public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = [];

    public int ModelCalls { get; init; }

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }

    public int SchemaFailures { get; init; }

    /// <summary>The findings as sent (or, for a shadow run, as they would have been sent), in contract JSON.</summary>
    public required string Findings { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }
}
