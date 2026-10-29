using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Runtime.Application.Abstractions.Persistence;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Memory;

public sealed class GetIncidentMemoryQueryHandler(IIncidentMemoryStore memory) : IQueryHandler<GetIncidentMemoryQuery, IncidentMemoryView?>
{
    public async Task<Result<IncidentMemoryView?>> Handle(GetIncidentMemoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await memory.GetAsync(query.IncidentId, cancellationToken));
    }
}
