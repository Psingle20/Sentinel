using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Api.Filters;

/// <summary>
/// Incident routes answer only for incidents of the caller's tenants. Another tenant's incident is
/// reported as not found, so its existence is not disclosed.
/// </summary>
public sealed class IncidentTenantFilter(IRuntimeIncidents incidents) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.RouteData.Values.TryGetValue("incidentId", out object? value) && Guid.TryParse(value?.ToString(), out Guid incidentId))
        {
            IncidentDetail? incident = await incidents.GetAsync(IncidentId.From(incidentId), context.HttpContext.RequestAborted);
            if (incident is null || !context.HttpContext.User.CanAccess(incident.Summary.TenantId))
            {
                context.Result = new NotFoundResult();
                return;
            }
        }

        await next();
    }
}