using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Api.Identity;
using Sentinel.Gateway.Application.Analytics;
using Sentinel.Gateway.Application.BusinessProcesses.Analytics;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>Product analytics from the web app: only catalogued events and properties are counted (Phase 08).</summary>
[Route(Routes.Root + "/analytics")]
[Authorize(Roles = Roles.Responder)]
public sealed class AnalyticsController(IRequestDispatcher requests) : ApiController
{
    [HttpPost]
    public async Task<IResult> Record(AnalyticsEvent analyticsEvent, CancellationToken cancellationToken) =>
        MatchAccepted(await requests.HandleRequestAsync(new RecordAnalyticsEventRequest(analyticsEvent), cancellationToken));
}
