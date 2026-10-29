using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessTasks.Commands.Deployments;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

public sealed class SetDeploymentFaultsProcessor(ICommandDispatcher commands) : IProcessor<SetDeploymentFaultsRequest>
{
    public Task<Result> Process(SetDeploymentFaultsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new SetDeploymentFaultsCommand(request.FailRollbacks), cancellationToken);
    }
}
