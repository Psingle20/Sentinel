using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.BusinessProcesses.Observations;

namespace Sentinel.Ingestion.Api.Controllers;

/// <summary>
/// The read-only observation query contract (ADR-007), used by AgentWorker tools and Runtime recovery
/// checks. Queries pass the bulkhead, so a burst cannot starve the ingestion worker (Phase 02).
/// </summary>
[Route(Routes.Root + "/observations")]
public sealed class ObservationsController(IRequestDispatcher requests) : ApiController
{
    [HttpPost("query")]
    public async Task<IResult> Query(ObservationQuery query, CancellationToken cancellationToken) =>
        MatchQuery(await requests.HandleRequestAsync(new QueryObservationsRequest(query), cancellationToken));
}
