using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Memory;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>
/// Incident queries for the Gateway, served from the read model; the run record, postmortem and
/// similar incidents (Phase 07); the authoritative state beside the projection's version (Phase 03);
/// and the read-only domain replay, safe on any incident at any time.
/// </summary>
[Route(Routes.Root + "/incidents")]
public sealed class IncidentsController(IncidentQueryService queries, PostmortemService postmortems, IncidentMemoryService memory, DomainReplayService replay) : ApiController
{
    [HttpGet]
    public async Task<IResult> List(int? limit, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListAsync(limit ?? 50, cancellationToken));

    [HttpGet("{incidentId:guid}")]
    public async Task<IResult> Get(Guid incidentId, CancellationToken cancellationToken) =>
        Match(await queries.GetAsync(IncidentId.From(incidentId), cancellationToken));

    [HttpGet("{incidentId:guid}/run-record")]
    public async Task<IResult> RunRecord(Guid incidentId, string? dataset, CancellationToken cancellationToken) =>
        await postmortems.RunRecordAsync(IncidentId.From(incidentId), dataset, cancellationToken) is { } record ? Results.Ok(record) : Results.NotFound();

    [HttpGet("{incidentId:guid}/postmortem")]
    public async Task<IResult> Postmortem(Guid incidentId, CancellationToken cancellationToken) =>
        await postmortems.BuildAsync(IncidentId.From(incidentId), cancellationToken) is { } postmortem ? Results.Ok(postmortem) : Results.NotFound();

    [HttpGet("{incidentId:guid}/similar")]
    public async Task<IResult> Similar(Guid incidentId, CancellationToken cancellationToken) =>
        await memory.SimilarToAsync(IncidentId.From(incidentId), cancellationToken) is { } similar ? Results.Ok(similar) : Results.NotFound();

    [HttpPost("{incidentId:guid}/replays/domain")]
    public async Task<IResult> ReplayDomain(Guid incidentId, CancellationToken cancellationToken) =>
        await replay.ReplayAsync(IncidentId.From(incidentId), cancellationToken) is { } result ? Results.Ok(result) : Results.NotFound();

    [HttpGet("{incidentId:guid}/state")]
    public async Task<IResult> State(Guid incidentId, CancellationToken cancellationToken) =>
        Match(await queries.StateAsync(IncidentId.From(incidentId), cancellationToken));
}
