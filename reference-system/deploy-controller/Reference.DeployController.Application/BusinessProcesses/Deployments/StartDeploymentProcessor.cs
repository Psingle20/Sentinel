using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessTasks.Commands.Deployments;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

public sealed class StartDeploymentProcessor(ICommandDispatcher commands) : IProcessor<StartDeploymentRequest, DeployResult>
{
    public Task<Result<DeployResult>> Process(StartDeploymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new StartDeploymentCommand(request.Service, request.Version, request.Kind, request.Actor, request.Reason, request.RolloutSeconds, request.ConfigurationDiff, request.IdempotencyKey), cancellationToken);
    }
}
