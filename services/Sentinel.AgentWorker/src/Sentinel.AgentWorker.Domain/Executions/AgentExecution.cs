using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Domain.Executions;

/// <summary>
/// One investigator run while it runs: the model calls and tokens it spends against its budget, the
/// tool calls it makes, how often its output was rejected, and how it ends. It finishes once, as
/// <see cref="ExecutionStateTransitions"/> allows, and its <see cref="ToRecord"/> is the audit record
/// AgentWorker keeps (ADR-007, Phase 07).
/// </summary>
public sealed class AgentExecution
{
    /// <summary>Rejected structured outputs the model may repair before the run abstains.</summary>
    public const int MaxRepairs = 1;

    private readonly List<ToolCallRecord> _toolCalls = [];

    private AgentExecution(
        AgentExecutionId agentExecutionId,
        TenantId tenantId,
        EnvironmentId environmentId,
        IncidentId incidentId,
        InvestigationId investigationId,
        string investigator,
        bool shadow,
        ExecutionModel model,
        DateTimeOffset startedAt)
    {
        AgentExecutionId = agentExecutionId;
        TenantId = tenantId;
        EnvironmentId = environmentId;
        IncidentId = incidentId;
        InvestigationId = investigationId;
        Investigator = investigator;
        Shadow = shadow;
        Model = model;
        StartedAt = startedAt;
    }

    public AgentExecutionId AgentExecutionId { get; }

    public TenantId TenantId { get; }

    public EnvironmentId EnvironmentId { get; }

    public IncidentId IncidentId { get; }

    public InvestigationId InvestigationId { get; }

    public string Investigator { get; }

    /// <summary>A shadow execution runs alongside the primary one for comparison and is never sent to Runtime.</summary>
    public bool Shadow { get; }

    public ExecutionModel Model { get; }

    public ExecutionState State { get; private set; } = ExecutionState.Running;

    /// <summary>Why the run abstained or failed.</summary>
    public string? AbstentionReason { get; private set; }

    public IReadOnlyList<ToolCallRecord> ToolCalls => _toolCalls;

    public int ModelCalls { get; private set; }

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public int SchemaFailures { get; private set; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static AgentExecution Start(
        AgentExecutionId agentExecutionId,
        TenantId tenantId,
        EnvironmentId environmentId,
        IncidentId incidentId,
        InvestigationId investigationId,
        string investigator,
        bool shadow,
        ExecutionModel model,
        DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(investigator);
        ArgumentNullException.ThrowIfNull(model);

        return new AgentExecution(agentExecutionId, tenantId, environmentId, incidentId, investigationId, investigator, shadow, model, startedAt);
    }

    /// <summary>
    /// A model call returned. Its tokens count against <paramref name="tokenBudget"/>, and a call that
    /// takes the run past it is refused: the run must abstain rather than spend more.
    /// </summary>
    public Result RecordModelCall(int inputTokens, int outputTokens, int tokenBudget)
    {
        if (State != ExecutionState.Running)
        {
            return Result.Failure(AgentExecutionErrors.Finished);
        }

        ModelCalls++;
        InputTokens += inputTokens;
        OutputTokens += outputTokens;
        return InputTokens + OutputTokens > tokenBudget ? Result.Failure(AgentExecutionErrors.TokenBudgetExhausted) : Result.Success();
    }

    /// <summary>Whether another tool call fits <paramref name="maxToolCalls"/>; past it, the tool is refused and the model told to conclude.</summary>
    public bool HasToolBudget(int maxToolCalls) => _toolCalls.Count < maxToolCalls;

    /// <summary>A tool call ran, or was refused, and returned <paramref name="call"/>'s result to the model.</summary>
    public Result RecordToolCall(ToolCallRecord call)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (State != ExecutionState.Running)
        {
            return Result.Failure(AgentExecutionErrors.Finished);
        }

        _toolCalls.Add(call);
        return Result.Success();
    }

    /// <summary>
    /// The model's structured output was rejected. Up to <see cref="MaxRepairs"/> rejections it may
    /// repair; the next is refused, and the run must abstain.
    /// </summary>
    public Result RecordSchemaFailure()
    {
        if (State != ExecutionState.Running)
        {
            return Result.Failure(AgentExecutionErrors.Finished);
        }

        SchemaFailures++;
        return SchemaFailures > MaxRepairs ? Result.Failure(AgentExecutionErrors.InvalidOutputAfterRepair) : Result.Success();
    }

    public Result Complete(DateTimeOffset completedAt) => Finish(ExecutionState.Completed, null, completedAt);

    public Result Abstain(string reason, DateTimeOffset completedAt) => Finish(ExecutionState.Abstained, reason, completedAt);

    public Result Fail(string reason, DateTimeOffset completedAt) => Finish(ExecutionState.Failed, reason, completedAt);

    /// <summary>The audit record of the finished run, with its findings as sent, or as they would have been for a shadow run.</summary>
    public AgentExecutionRecord ToRecord(string findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        if (CompletedAt is not { } completedAt)
        {
            throw new InvalidOperationException($"Execution {AgentExecutionId} is still running; only a finished execution has a record.");
        }

        return new AgentExecutionRecord
        {
            AgentExecutionId = AgentExecutionId,
            TenantId = TenantId,
            EnvironmentId = EnvironmentId,
            IncidentId = IncidentId,
            InvestigationId = InvestigationId,
            Investigator = Investigator,
            Shadow = Shadow,
            ModelProvider = Model.Provider,
            Model = Model.Model,
            ModelVersion = Model.ModelVersion,
            PromptVersion = Model.PromptVersion,
            Result = State switch
            {
                ExecutionState.Completed => ExecutionResult.Completed,
                ExecutionState.Abstained => ExecutionResult.Abstained,
                _ => ExecutionResult.Failed
            },
            AbstentionReason = AbstentionReason,
            ToolCalls = [.. _toolCalls],
            ModelCalls = ModelCalls,
            InputTokens = InputTokens,
            OutputTokens = OutputTokens,
            SchemaFailures = SchemaFailures,
            Findings = findings,
            StartedAt = StartedAt,
            CompletedAt = completedAt
        };
    }

    private Result Finish(ExecutionState state, string? reason, DateTimeOffset completedAt)
    {
        if (!ExecutionStateTransitions.CanMove(State, state))
        {
            return Result.Failure(AgentExecutionErrors.Finished);
        }

        State = state;
        AbstentionReason = reason;
        CompletedAt = completedAt;
        return Result.Success();
    }
}
