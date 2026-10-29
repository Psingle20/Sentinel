using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Api.Filters;
using Sentinel.Gateway.Api.Identity;
using Sentinel.Gateway.Application.BusinessProcesses.Incidents;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>Incidents from Runtime, in the caller's tenants, with their pages, records, memory and agent executions (Phases 07 and 08).</summary>
[Route(Routes.Incidents)]
[Authorize(Roles = Roles.Responder)]
[IncidentTenant]
public sealed class IncidentsController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    public async Task<IResult> List(int? limit, CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new ListIncidentsRequest(limit ?? 50, User.Tenants()), cancellationToken));

    [HttpGet("{incidentId:guid}")]
    public async Task<IResult> Get(Guid incidentId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetIncidentRequest(IncidentId.From(incidentId)), cancellationToken));

    [HttpGet("{incidentId:guid}/timeline")]
    public async Task<IResult> Timeline(Guid incidentId, long? before, int? limit, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetIncidentTimelineRequest(IncidentId.From(incidentId), before, limit), cancellationToken));

    [HttpGet("{incidentId:guid}/evidence")]
    public async Task<IResult> Evidence(Guid incidentId, int? offset, int? limit, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetIncidentEvidenceRequest(IncidentId.From(incidentId), offset, limit), cancellationToken));

    [HttpGet("{incidentId:guid}/run-record")]
    public async Task<IResult> RunRecord(Guid incidentId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetRunRecordRequest(IncidentId.From(incidentId)), cancellationToken));

    [HttpGet("{incidentId:guid}/similar")]
    public async Task<IResult> Similar(Guid incidentId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetSimilarIncidentsRequest(IncidentId.From(incidentId)), cancellationToken));

    [HttpPost("{incidentId:guid}/replays/domain")]
    public async Task<IResult> ReplayDomain(Guid incidentId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new ReplayIncidentDomainRequest(IncidentId.From(incidentId)), cancellationToken));

    [HttpGet("{incidentId:guid}/agent-executions")]
    public async Task<IResult> AgentExecutions(Guid incidentId, CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new ListIncidentAgentExecutionsRequest(IncidentId.From(incidentId)), cancellationToken));
}
