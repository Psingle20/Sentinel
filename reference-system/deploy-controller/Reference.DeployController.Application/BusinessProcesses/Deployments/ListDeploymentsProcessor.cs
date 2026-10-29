using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessTasks.Queries.Deployments;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

public sealed class ListDeploymentsProcessor(IQueryDispatcher queries) : IProcessor<ListDeploymentsRequest, IReadOnlyList<DeploymentRecord>>
{
    public Task<Result<IReadOnlyList<DeploymentRecord>>> Process(ListDeploymentsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new ListDeploymentsQuery(request.Since, request.Until, request.Service), cancellationToken);
    }
}
