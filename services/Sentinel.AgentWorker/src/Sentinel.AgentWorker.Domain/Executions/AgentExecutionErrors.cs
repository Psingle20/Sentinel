using CleanArchitecture.BuildingBlocks;

namespace Sentinel.AgentWorker.Domain.Executions;

/// <summary>
/// Why an execution refuses to go on. The descriptions are the abstention reasons and tool results
/// investigators have always given, so a refusal reads the same to Runtime and to the model.
/// </summary>
public static class AgentExecutionErrors
{
    public static readonly Error TokenBudgetExhausted = Error.Problem("AgentExecution.TokenBudgetExhausted", "The model token budget was exhausted.");

    public static readonly Error InvalidOutputAfterRepair = Error.Problem("AgentExecution.InvalidOutputAfterRepair", "The model's output was invalid after a repair attempt");

    public static readonly Error Finished = Error.Conflict("AgentExecution.Finished", "The execution has finished; it records nothing more.");

    public static Error ToolBudgetExhausted(int maxToolCalls) =>
        Error.Problem("AgentExecution.ToolBudgetExhausted", $"The tool budget of {maxToolCalls} calls is exhausted; conclude from what you have.");
}
