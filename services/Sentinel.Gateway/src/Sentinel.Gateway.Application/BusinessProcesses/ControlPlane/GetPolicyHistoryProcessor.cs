using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class GetPolicyHistoryProcessor(IQueryDispatcher queries) : IProcessor<GetPolicyHistoryRequest, ControlPlaneResponse>
{
    public Task<Result<ControlPlaneResponse>> Process(GetPolicyHistoryRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetPolicyHistoryQuery(), cancellationToken);
    }
}
