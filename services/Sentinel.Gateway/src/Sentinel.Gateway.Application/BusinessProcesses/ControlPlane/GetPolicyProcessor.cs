using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class GetPolicyProcessor(IQueryDispatcher queries) : IProcessor<GetPolicyRequest, ControlPlaneResponse>
{
    public Task<Result<ControlPlaneResponse>> Process(GetPolicyRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetPolicyQuery(), cancellationToken);
    }
}
