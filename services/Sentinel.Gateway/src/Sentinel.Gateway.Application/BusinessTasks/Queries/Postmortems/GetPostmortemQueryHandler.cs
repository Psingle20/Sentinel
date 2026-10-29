using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Postmortems;

/// <summary>
/// Postmortems (Phase 07): Runtime generates the structured part from the incident's history, with a
/// source for every fact; the responders' narrative, which the Gateway stores, comes with it.
/// </summary>
public sealed class GetPostmortemQueryHandler(IRuntimeHistory history, INarrativeStore narratives) : IQueryHandler<GetPostmortemQuery, Postmortem?>
{
    public async Task<Result<Postmortem?>> Handle(GetPostmortemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Postmortem? postmortem = await history.PostmortemAsync(query.IncidentId, cancellationToken);
        return Result.Success(postmortem is null ? null : postmortem with { Narrative = await narratives.GetAsync(query.IncidentId, cancellationToken) });
    }
}
