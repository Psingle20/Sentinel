using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Memory;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>Incident memory (Phase 07): one record per closed incident.</summary>
[Route(Routes.Root + "/memory")]
public sealed class MemoryController(IncidentMemoryService memory) : ApiController
{
    [HttpGet("{incidentId:guid}")]
    public async Task<IResult> Get(Guid incidentId, CancellationToken cancellationToken) =>
        await memory.GetAsync(IncidentId.From(incidentId), cancellationToken) is { } record ? Results.Ok(record) : Results.NotFound();
}