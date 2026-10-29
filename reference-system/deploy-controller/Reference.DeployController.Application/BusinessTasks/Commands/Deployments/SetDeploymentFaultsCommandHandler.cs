using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.Services;

namespace Reference.DeployController.Application.BusinessTasks.Commands.Deployments;

public sealed class SetDeploymentFaultsCommandHandler(DeploymentFaults faults) : ICommandHandler<SetDeploymentFaultsCommand>
{
    public Task<Result> Handle(SetDeploymentFaultsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        faults.FailRollbacks = command.FailRollbacks;
        return Task.FromResult(Result.Success());
    }
}
