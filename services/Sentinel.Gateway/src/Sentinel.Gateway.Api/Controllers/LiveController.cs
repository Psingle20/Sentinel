using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Application.Live;

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
    public IResult Events(long? after, CancellationToken cancellationToken)
    {
        long? resume = long.TryParse(Request.Headers["Last-Event-ID"].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out long lastEventId) ? lastEventId : after;
        return TypedResults.ServerSentEvents(LiveEventStream.ReadAsync(events, User.Tenants(), resume, cancellationToken));
    }
}