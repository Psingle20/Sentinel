using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessTasks.Queries.Services;

namespace Reference.DeployController.Application.BusinessProcesses.Services;

public sealed class GetServiceProcessor(IQueryDispatcher queries) : IProcessor<GetServiceRequest, ServiceView?>
{
    public Task<Result<ServiceView?>> Process(GetServiceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetServiceQuery(request.Name), cancellationToken);
    }
}
