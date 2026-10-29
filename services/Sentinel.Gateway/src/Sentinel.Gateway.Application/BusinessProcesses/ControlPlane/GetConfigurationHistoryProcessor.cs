using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class GetConfigurationHistoryProcessor(IQueryDispatcher queries) : IProcessor<GetConfigurationHistoryRequest, ControlPlaneResponse>
{
    public Task<Result<ControlPlaneResponse>> Process(GetConfigurationHistoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetConfigurationHistoryQuery(request.Area), cancellationToken);
    }
}
