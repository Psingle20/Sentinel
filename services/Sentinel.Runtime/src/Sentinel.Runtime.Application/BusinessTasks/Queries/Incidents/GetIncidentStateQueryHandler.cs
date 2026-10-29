using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Projections;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;

/// <summary>The authoritative state from the incident's writer beside the version its projection was built from.</summary>
public sealed class GetIncidentStateQueryHandler(IIncidentStore incidents, IIncidentOwner owner) : IQueryHandler<GetIncidentStateQuery, IncidentStateView>
{
    public async Task<Result<IncidentStateView>> Handle(GetIncidentStateQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IncidentSnapshot? snapshot = await owner.GetAsync(query.IncidentId, cancellationToken);
        if (snapshot is null)
        {
            return Result.Failure<IncidentStateView>(IncidentErrors.NotFound);
        }

        long? projected = await incidents.ProjectionVersionAsync(query.IncidentId, cancellationToken);
        return Result.Success(new IncidentStateView(
            snapshot.IncidentId,
            snapshot.State.ToStatus(),
            snapshot.Version,
            projected,
            projected is { } version ? snapshot.Version - version : null,
            snapshot.Cycle,
            snapshot.PendingInvestigationIds));
    }
}
