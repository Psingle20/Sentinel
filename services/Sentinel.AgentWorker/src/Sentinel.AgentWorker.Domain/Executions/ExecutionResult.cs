using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Domain.Executions;

public enum ExecutionResult
{
    Completed,
    Abstained,
    Failed
}
