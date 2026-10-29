using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.BusinessTasks.Queries.Policy;
using Sentinel.Executor.Application.Abstractions.Persistence;

namespace Sentinel.Executor.Application.BusinessProcesses.Policy;

public sealed class GetPolicyHistoryProcessor(IQueryDispatcher queries) : IProcessor<GetPolicyHistoryRequest, IReadOnlyList<PolicyVersionInfo>>
{
    public Task<Result<IReadOnlyList<PolicyVersionInfo>>> Process(GetPolicyHistoryRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetPolicyHistoryQuery(), cancellationToken);
    }
}
