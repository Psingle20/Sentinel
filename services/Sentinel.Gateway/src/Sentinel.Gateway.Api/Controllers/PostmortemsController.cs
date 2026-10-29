using CleanArchitecture.BuildingBlocks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Api.Contracts;
using Sentinel.Gateway.Api.Filters;
using Sentinel.Gateway.Application.Postmortems;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>An incident's postmortem with its narrative (Phase 07).</summary>
[Route(Routes.Incident + "/postmortem")]
[Authorize(Roles = Roles.Responder)]
[IncidentTenant]
public sealed class PostmortemsController(PostmortemService postmortems) : ApiController
{
    [HttpGet]
    public async Task<IResult> Get(Guid incidentId, CancellationToken cancellationToken) =>
        await postmortems.GetAsync(IncidentId.From(incidentId), cancellationToken) is { } postmortem ? Results.Ok(postmortem) : Results.NotFound();

    [HttpPut("narrative")]
    public async Task<IResult> SaveNarrative(Guid incidentId, SaveNarrativeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<PostmortemNarrative> result = await postmortems.SaveNarrativeAsync(
            new SaveNarrative(IncidentId.From(incidentId), body.Markdown, body.ExpectedVersion, User.UserId()),
            cancellationToken);
        return Match(result);
    }
}