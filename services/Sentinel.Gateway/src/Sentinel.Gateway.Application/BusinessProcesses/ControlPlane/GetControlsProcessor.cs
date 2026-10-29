using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class GetControlsProcessor(IQueryDispatcher queries) : IProcessor<GetControlsRequest, ControlPlaneResponse>
{
    public Task<Result<ControlPlaneResponse>> Process(GetControlsRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetControlsQuery(), cancellationToken);
    }
}
