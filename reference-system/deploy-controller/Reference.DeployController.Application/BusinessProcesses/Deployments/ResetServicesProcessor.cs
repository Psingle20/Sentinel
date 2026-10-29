using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessTasks.Commands.Deployments;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

public sealed class ResetServicesProcessor(ICommandDispatcher commands) : IProcessor<ResetServicesRequest, IReadOnlyList<DeploymentRecord>>
{
    public Task<Result<IReadOnlyList<DeploymentRecord>>> Process(ResetServicesRequest request, CancellationToken cancellationToken)
    {
        return commands.Dispatch(new ResetServicesCommand(), cancellationToken);
    }
}
