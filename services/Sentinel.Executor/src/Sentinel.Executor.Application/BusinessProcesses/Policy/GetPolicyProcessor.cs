using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.BusinessTasks.Queries.Policy;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessProcesses.Policy;

public sealed class GetPolicyProcessor(IQueryDispatcher queries) : IProcessor<GetPolicyRequest, PolicyDocument>
{
    public Task<Result<PolicyDocument>> Process(GetPolicyRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetPolicyQuery(), cancellationToken);
    }
}
