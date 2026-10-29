using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Api.Identity;
using Sentinel.ServiceDefaults;

namespace Sentinel.Gateway.Api.Filters;

/// <summary>
/// Incident routes answer only for incidents of the caller's tenants. Another tenant's incident is
/// reported as not found, so its existence is not disclosed. An incident the caller may see puts its
/// tenant, environment and ID on the request's span and in the log scope of the action, as every
/// message's identity headers do.
/// </summary>
public sealed class IncidentTenantFilter(IRuntimeIncidents incidents, ILogger<IncidentTenantFilter> logger) : IAsyncActionFilter
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

            var identity = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [SentinelTags.TenantId] = incident.Summary.TenantId.Value,
                [SentinelTags.EnvironmentId] = incident.Summary.EnvironmentId.Value,
                [SentinelTags.IncidentId] = incident.Summary.IncidentId.ToString()
            };
            foreach ((string name, object? tag) in identity)
            {
                Activity.Current?.SetTag(name, tag);
            }

            using (logger.BeginScope(identity))
            {
                await next();
            }

            return;
        }

        await next();
    }
}
