using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessTasks.Queries.Services;

namespace Reference.DeployController.Application.BusinessProcesses.Services;

public sealed class ListServicesProcessor(IQueryDispatcher queries) : IProcessor<ListServicesRequest, IReadOnlyList<ServiceView>>
{
    public Task<Result<IReadOnlyList<ServiceView>>> Process(ListServicesRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new ListServicesQuery(), cancellationToken);
    }
}
