using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Api;
using Sentinel.Gateway.Api.Filters;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;
using Sentinel.Gateway.Api.Identity;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Options;
using Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;
using Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Live;

namespace Sentinel.Gateway.Tests;

public sealed class TenantScopeTests
{
    private static readonly IncidentId Acme = IncidentId.New();

    [Fact]
    public async Task An_incident_of_another_tenant_is_not_found_rather_than_forbidden()
    {
        var filter = new IncidentTenantFilter(new OneIncident(Acme, TenantId.From("acme")));

        (await InvokeAsync(filter, Acme, ["acme"])).ShouldBeNull();
        (await InvokeAsync(filter, Acme, ["globex"])).ShouldBeOfType<NotFoundResult>();
        (await InvokeAsync(filter, IncidentId.New(), ["acme"])).ShouldBeOfType<NotFoundResult>();
    }

    // The action's result: null when the filter let the action run, otherwise what the filter answered instead.
    private static async Task<IActionResult?> InvokeAsync(IncidentTenantFilter filter, IncidentId incidentId, string[] tenants)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([.. tenants.Select(tenant => new Claim(SentinelClaims.Tenant, tenant))], "test"))
        };
        var route = new RouteData();
        route.Values["incidentId"] = incidentId.Value.ToString();
        var action = new ActionContext(http, route, new ActionDescriptor());
        var context = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), controller: new object());
        await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(action, [], new object())));
        return context.Result;
    }

    private sealed class OneIncident(IncidentId known, TenantId tenantId) : IRuntimeIncidents
    {
        public Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IncidentSummary>>([]);

        public Task<IncidentDetail?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
            Task.FromResult(incidentId != known ? null : new IncidentDetail
            {
                Summary = new IncidentSummary
                {
                    TenantId = tenantId,
                    EnvironmentId = EnvironmentId.From("reference"),
                    IncidentId = known,
                    AffectedServiceId = ServiceId.From("checkout-service"),
                    Title = "checkout burn",
                    Severity = Severity.High,
                    Status = IncidentStatus.Investigating,
                    OpenedAt = DateTimeOffset.UnixEpoch,
                    UpdatedAt = DateTimeOffset.UnixEpoch,
                    StreamVersion = 1
                }
            });

        public Task<IncidentRunRecord?> RunRecordAsync(IncidentId incidentId, CancellationToken cancellationToken) => Task.FromResult<IncidentRunRecord?>(null);
    }
}
