using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class GetConfigurationProcessor(IQueryDispatcher queries) : IProcessor<GetConfigurationRequest, ControlPlaneResponse>
{
    public Task<Result<ControlPlaneResponse>> Process(GetConfigurationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetConfigurationQuery(request.Area), cancellationToken);
    }
}
