using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;

/// <summary>Runtime's most recent incidents, at most 200, keeping only those in the caller's tenants.</summary>
public sealed class ListIncidentsQueryHandler(IRuntimeIncidents incidents) : IQueryHandler<ListIncidentsQuery, IReadOnlyList<IncidentSummary>>
{
    public const int MaxLimit = 200;

    public async Task<Result<IReadOnlyList<IncidentSummary>>> Handle(ListIncidentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<IncidentSummary> recent = await incidents.ListAsync(Math.Clamp(query.Limit, 1, MaxLimit), cancellationToken);
        return Result.Success<IReadOnlyList<IncidentSummary>>([.. recent.Where(incident => query.Tenants.Contains(incident.TenantId))]);
    }
}
