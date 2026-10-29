using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Api.Filters;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.Incidents;
using Sentinel.Gateway.Application.Postmortems;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Api.Identity;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>Runtime's incidents, served within the caller's tenants (ADR-001); long timelines and evidence sets one page at a time (Phase 08).</summary>
[Route(Routes.Incidents)]
[Authorize(Roles = Roles.Responder)]
[IncidentTenant]
public sealed class IncidentsController(IRuntimeIncidents incidents, IRuntimeHistory history, IAgentExecutions executions) : ApiController
{
    [HttpGet]
    public async Task<IResult> List(int? limit, CancellationToken cancellationToken) =>
        Results.Ok((await incidents.ListAsync(Math.Clamp(limit ?? 50, 1, 200), cancellationToken)).Where(incident => User.CanAccess(incident.TenantId)));

    [HttpGet("{incidentId:guid}")]
    public async Task<IResult> Get(Guid incidentId, CancellationToken cancellationToken) =>
        await incidents.GetAsync(IncidentId.From(incidentId), cancellationToken) is { } detail ? Results.Ok(detail) : Results.NotFound();

    [HttpGet("{incidentId:guid}/timeline")]
    public async Task<IResult> Timeline(Guid incidentId, long? before, int? limit, CancellationToken cancellationToken) =>
        await incidents.GetAsync(IncidentId.From(incidentId), cancellationToken) is { } detail ? Results.Ok(Pages.Timeline(detail.Timeline ?? [], before, limit)) : Results.NotFound();

    [HttpGet("{incidentId:guid}/evidence")]
    public async Task<IResult> Evidence(Guid incidentId, int? offset, int? limit, CancellationToken cancellationToken) =>
        await incidents.GetAsync(IncidentId.From(incidentId), cancellationToken) is { } detail ? Results.Ok(Pages.Evidence(detail.Evidence ?? [], offset, limit)) : Results.NotFound();

    [HttpGet("{incidentId:guid}/run-record")]
    public async Task<IResult> RunRecord(Guid incidentId, CancellationToken cancellationToken) =>
        await incidents.RunRecordAsync(IncidentId.From(incidentId), cancellationToken) is { } record ? Results.Ok(record) : Results.NotFound();

    [HttpGet("{incidentId:guid}/similar")]
    public async Task<IResult> Similar(Guid incidentId, CancellationToken cancellationToken) =>
        await history.SimilarAsync(IncidentId.From(incidentId), cancellationToken) is { } similar ? Results.Ok(similar) : Results.NotFound();

    [HttpPost("{incidentId:guid}/replays/domain")]
    public async Task<IResult> ReplayDomain(Guid incidentId, CancellationToken cancellationToken) =>
        await history.ReplayAsync(IncidentId.From(incidentId), cancellationToken) is { } replay ? Results.Ok(replay) : Results.NotFound();

    [HttpGet("{incidentId:guid}/agent-executions")]
    public async Task<IResult> AgentExecutions(Guid incidentId, CancellationToken cancellationToken) =>
        Results.Ok(await executions.ForIncidentAsync(IncidentId.From(incidentId), cancellationToken));
}
