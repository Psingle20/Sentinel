using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Approvals;

namespace Sentinel.Gateway.Application.BusinessProcesses.Approvals;

public sealed class GetApprovalKeysProcessor(IQueryDispatcher queries) : IProcessor<GetApprovalKeysRequest, ApprovalKeySet>
{
    public Task<Result<ApprovalKeySet>> Process(GetApprovalKeysRequest request, CancellationToken cancellationToken) =>
        queries.Dispatch(new GetApprovalKeysQuery(), cancellationToken);
}
