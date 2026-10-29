using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.Abstractions.Persistence;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Policy;

public sealed class GetPolicyHistoryQueryHandler(IPolicyStore policies) : IQueryHandler<GetPolicyHistoryQuery, IReadOnlyList<PolicyVersionInfo>>
{
    public async Task<Result<IReadOnlyList<PolicyVersionInfo>>> Handle(GetPolicyHistoryQuery query, CancellationToken cancellationToken) =>
        Result.Success(await policies.HistoryAsync(cancellationToken));
}
