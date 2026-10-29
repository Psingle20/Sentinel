using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class GetSourceHealthProcessor(IQueryDispatcher queries) : IProcessor<GetSourceHealthRequest, ControlPlaneResponse>
{
    public Task<Result<ControlPlaneResponse>> Process(GetSourceHealthRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetSourceHealthQuery(), cancellationToken);
    }
}
