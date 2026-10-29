namespace Reference.DeployController.Domain.Revisions;

/// <summary>A deployment in progress finishes once, by completing or failing; a finished one never changes again.</summary>
public static class DeploymentStatusTransitions
{
    private static readonly Dictionary<DeploymentStatus, DeploymentStatus[]> Transitions = new()
    {
        [DeploymentStatus.InProgress] = [DeploymentStatus.Completed, DeploymentStatus.Failed],
        [DeploymentStatus.Completed] = [],
        [DeploymentStatus.Failed] = []
    };

    public static bool CanMove(DeploymentStatus from, DeploymentStatus to) => Transitions[from].Contains(to);
}
