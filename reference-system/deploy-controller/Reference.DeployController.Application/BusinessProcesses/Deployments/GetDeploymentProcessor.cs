using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessTasks.Queries.Deployments;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

public sealed class GetDeploymentProcessor(IQueryDispatcher queries) : IProcessor<GetDeploymentRequest, DeploymentRecord?>
{
    public Task<Result<DeploymentRecord?>> Process(GetDeploymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetDeploymentQuery(request.DeploymentId), cancellationToken);
    }
}
