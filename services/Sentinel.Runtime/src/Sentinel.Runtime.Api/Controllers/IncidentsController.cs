using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.BusinessProcesses.Incidents;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>
/// Incident queries for the Gateway, served from the read model; the run record, postmortem and
/// similar incidents (Phase 07); the authoritative state beside the projection's version (Phase 03);
/// and the read-only domain replay, safe on any incident at any time.
/// </summary>
[Route(Routes.Root + "/incidents")]
public sealed class IncidentsController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    public async Task<IResult> List(int? limit, CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new ListIncidentsRequest(limit ?? 50), cancellationToken));

    [HttpGet("{incidentId:guid}")]
    public async Task<IResult> Get(Guid incidentId, CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetIncidentRequest(IncidentId.From(incidentId)), cancellationToken));

    [HttpGet("{incidentId:guid}/run-record")]
    public async Task<IResult> RunRecord(Guid incidentId, string? dataset, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetRunRecordRequest(IncidentId.From(incidentId), dataset), cancellationToken));

    [HttpGet("{incidentId:guid}/postmortem")]
    public async Task<IResult> Postmortem(Guid incidentId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetPostmortemRequest(IncidentId.From(incidentId)), cancellationToken));

    [HttpGet("{incidentId:guid}/similar")]
    public async Task<IResult> Similar(Guid incidentId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetSimilarIncidentsRequest(IncidentId.From(incidentId)), cancellationToken));

    [HttpPost("{incidentId:guid}/replays/domain")]
    public async Task<IResult> ReplayDomain(Guid incidentId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new ReplayDomainRequest(IncidentId.From(incidentId)), cancellationToken));

    [HttpGet("{incidentId:guid}/state")]
    public async Task<IResult> State(Guid incidentId, CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetIncidentStateRequest(IncidentId.From(incidentId)), cancellationToken));
}
