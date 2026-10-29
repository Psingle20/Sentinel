using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;

/// <summary>Runtime replays the decisions from the incident's stream; the Gateway records nothing.</summary>
public sealed class ReplayIncidentDomainQueryHandler(IRuntimeHistory history) : IQueryHandler<ReplayIncidentDomainQuery, DomainReplayResult?>
{
    public async Task<Result<DomainReplayResult?>> Handle(ReplayIncidentDomainQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await history.ReplayAsync(query.IncidentId, cancellationToken));
    }
}
