using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Application.Analytics;
using Sentinel.Gateway.Api.Identity;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>Product analytics (Phase 08): approved events only, counted per day without who, which incident or which tenant.</summary>
[Route(Routes.Root + "/analytics")]
[Authorize(Roles = Roles.Responder)]
public sealed class AnalyticsController(ProductAnalyticsService analytics) : ApiController
{
    [HttpPost]
    public async Task<IResult> Record(AnalyticsEvent analyticsEvent, CancellationToken cancellationToken) =>
        MatchAccepted(await analytics.RecordAsync(analyticsEvent, cancellationToken));
}
