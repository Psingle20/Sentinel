using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Policy;

public sealed class GetPolicyQueryHandler(IPolicyStore policies) : IQueryHandler<GetPolicyQuery, PolicyDocument>
{
    public async Task<Result<PolicyDocument>> Handle(GetPolicyQuery query, CancellationToken cancellationToken) =>
        Result.Success(await policies.CurrentAsync(cancellationToken));
}
