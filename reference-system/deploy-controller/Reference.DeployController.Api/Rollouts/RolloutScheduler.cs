using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.Abstractions;
using Reference.DeployController.Application.BusinessProcesses.Deployments;

namespace Reference.DeployController.Api.Rollouts;

/// <summary>
/// Completes each rollout on a background task once its rollout time has passed, through the
/// application in a scope of its own. A rollout cut short by shutdown stays in progress.
/// </summary>
public sealed class RolloutScheduler(IServiceScopeFactory scopes, TimeProvider time, IHostApplicationLifetime lifetime) : IRolloutScheduler
{
    public void Schedule(string deploymentId, TimeSpan rollout) =>
        _ = Task.Run(() => CompleteAsync(deploymentId, rollout), CancellationToken.None);

    private async Task CompleteAsync(string deploymentId, TimeSpan rollout)
    {
        CancellationToken stopping = lifetime.ApplicationStopping;
        try
        {
            await Task.Delay(rollout, time, stopping);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return;
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(new CompleteRolloutRequest(deploymentId), stopping);
    }
}
