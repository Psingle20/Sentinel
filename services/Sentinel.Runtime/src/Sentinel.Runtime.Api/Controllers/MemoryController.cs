using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.BusinessProcesses.Memory;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>Incident memory (Phase 07): one record per closed incident.</summary>
[Route(Routes.Root + "/memory")]
public sealed class MemoryController(IRequestDispatcher requests) : ApiController
{
    [HttpGet("{incidentId:guid}")]
    public async Task<IResult> Get(Guid incidentId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetIncidentMemoryRequest(IncidentId.From(incidentId)), cancellationToken));
}
