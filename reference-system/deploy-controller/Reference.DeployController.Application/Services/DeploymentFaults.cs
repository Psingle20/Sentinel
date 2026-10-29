namespace Reference.DeployController.Application.Services;

/// <summary>
/// Faults a scenario can inject (Phase 07 failure scenarios). With <see cref="FailRollbacks"/> set, a
/// rollback is accepted but its rollout fails, as a real rollback can; reset clears every fault.
/// </summary>
public sealed class DeploymentFaults
{
    private volatile bool _failRollbacks;

    public bool FailRollbacks
    {
        get => _failRollbacks;
        set => _failRollbacks = value;
    }
}
