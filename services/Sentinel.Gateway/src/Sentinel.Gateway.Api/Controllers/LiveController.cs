using System.Globalization;
using System.Net.ServerSentEvents;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Api.Identity;
using Sentinel.Gateway.Application.BusinessProcesses.Live;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>
/// Live incident events as server-sent events (Phase 08), in the caller's tenants; a reconnecting client
/// resumes after the last event it received.
/// </summary>
[Route(Routes.Root + "/events")]
[Authorize(Roles = Roles.Responder)]
public sealed class LiveController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    public async Task<IResult> Events(long? after, [FromHeader(Name = "Last-Event-ID")] string? lastEventId, CancellationToken cancellationToken)
    {
        long? resume = long.TryParse(lastEventId, NumberStyles.None, CultureInfo.InvariantCulture, out long resumeAfter) ? resumeAfter : after;
        Result<IAsyncEnumerable<LiveEvent>> events = await requests.HandleRequestAsync(new StreamLiveEventsRequest(User.Tenants(), resume), cancellationToken);
        return events.IsSuccess ? TypedResults.ServerSentEvents(Items(events.Value)) : Problem(events.Error);
    }

    private static async IAsyncEnumerable<SseItem<LiveEvent>> Items(IAsyncEnumerable<LiveEvent> events)
    {
        await foreach (LiveEvent live in events)
        {
            yield return new SseItem<LiveEvent>(live, "incident") { EventId = live.Sequence.ToString(CultureInfo.InvariantCulture) };
        }
    }
}
