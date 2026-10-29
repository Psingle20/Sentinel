namespace Reference.DeployController.Application.Abstractions;

/// <summary>Completes a rollout after it has run for <c>rollout</c>, unless the controller stops first.</summary>
public interface IRolloutScheduler
{
    void Schedule(string deploymentId, TimeSpan rollout);
}
