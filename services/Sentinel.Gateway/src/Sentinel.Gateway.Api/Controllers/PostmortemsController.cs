using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Api.Contracts;
using Sentinel.Gateway.Api.Filters;
using Sentinel.Gateway.Api.Identity;
using Sentinel.Gateway.Application.BusinessProcesses.Postmortems;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>An incident's postmortem (Phase 07): Runtime's generated facts and the responders' versioned narrative.</summary>
[Route(Routes.Incident + "/postmortem")]
[Authorize(Roles = Roles.Responder)]
[IncidentTenant]
public sealed class PostmortemsController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    public async Task<IResult> Get(Guid incidentId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetPostmortemRequest(IncidentId.From(incidentId)), cancellationToken));

    [HttpPut("narrative")]
    public async Task<IResult> SaveNarrative(Guid incidentId, SaveNarrativeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Match(await requests.HandleRequestAsync(new SaveNarrativeRequest(IncidentId.From(incidentId), body.Markdown, body.ExpectedVersion, User.UserId()), cancellationToken));
    }
}
