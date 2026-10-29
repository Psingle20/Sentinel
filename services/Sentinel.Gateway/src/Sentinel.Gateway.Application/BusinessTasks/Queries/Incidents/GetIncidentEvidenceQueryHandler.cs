using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.Incidents;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;

public sealed class GetIncidentEvidenceQueryHandler(IRuntimeIncidents incidents) : IQueryHandler<GetIncidentEvidenceQuery, Page<EvidenceView>?>
{
    public async Task<Result<Page<EvidenceView>?>> Handle(GetIncidentEvidenceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IncidentDetail? detail = await incidents.GetAsync(query.IncidentId, cancellationToken);
        return Result.Success(detail is null ? null : Pages.Evidence(detail.Evidence ?? [], query.Offset, query.Limit));
    }
}
