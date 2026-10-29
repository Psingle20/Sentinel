namespace Sentinel.AgentWorker.Domain.Executions;

/// <summary>
/// The moves an execution may make, as a table: a running execution finishes once, by completing,
/// abstaining or failing, and a finished one never moves again. <see cref="AgentExecution"/> checks
/// this table and never compares its state inline.
/// </summary>
public static class ExecutionStateTransitions
{
    private static readonly Dictionary<ExecutionState, ExecutionState[]> Transitions = new()
    {
        [ExecutionState.Running] = [ExecutionState.Completed, ExecutionState.Abstained, ExecutionState.Failed],
        [ExecutionState.Completed] = [],
        [ExecutionState.Abstained] = [],
        [ExecutionState.Failed] = []
    };

    /// <summary>Whether an execution in <paramref name="from"/> may move to <paramref name="to"/>.</summary>
    public static bool CanMove(ExecutionState from, ExecutionState to) => Transitions[from].Contains(to);
}
