namespace Sentinel.AgentWorker.Domain.Executions;

/// <summary>Where an investigator run is: running, or finished in one of three ways.</summary>
public enum ExecutionState
{
    Running,
    Completed,
    Abstained,
    Failed
}
