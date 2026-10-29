using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;

/// <summary>Executor's answer, passed through with its status code and problem.</summary>
public sealed class GetPolicyHistoryQueryHandler(IExecutorControlPlane executor) : IQueryHandler<GetPolicyHistoryQuery, ControlPlaneResponse>
{
    public async Task<Result<ControlPlaneResponse>> Handle(GetPolicyHistoryQuery query, CancellationToken cancellationToken) =>
        Result.Success(await executor.PolicyHistoryAsync(cancellationToken));
}
