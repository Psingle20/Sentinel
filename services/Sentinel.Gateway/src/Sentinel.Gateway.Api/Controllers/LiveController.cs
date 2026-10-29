using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Api.Live;
using Sentinel.Gateway.Api.Identity;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>
/// Live updates (Phase 08): server-sent events for the caller's tenants, resumable from
/// <c>Last-Event-ID</c>. Each event names what changed; the query endpoints stay the source of truth.
/// </summary>
[Route(Routes.Root + "/events")]
[Authorize(Roles = Roles.Responder)]
public sealed class LiveController(ILiveEventStore events) : ApiController
{
    [HttpGet]
    public IResult Events(long? after, [FromHeader(Name = "Last-Event-ID")] string? lastEventId, CancellationToken cancellationToken)
    {
        long? resume = long.TryParse(lastEventId, NumberStyles.None, CultureInfo.InvariantCulture, out long resumeAfter) ? resumeAfter : after;
        return TypedResults.ServerSentEvents(LiveEventStream.ReadAsync(events, User.Tenants(), resume, cancellationToken));
    }
}
