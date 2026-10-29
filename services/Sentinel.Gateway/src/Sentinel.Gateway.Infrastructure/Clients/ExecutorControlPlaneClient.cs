using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Gateway.Application.Analytics;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Application.Postmortems;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Infrastructure.Clients;

internal sealed class ExecutorControlPlaneClient(HttpClient http) : IExecutorControlPlane
{
    public Task<ControlPlaneResponse> PolicyAsync(CancellationToken cancellationToken) => ControlPlaneHttp.SendAsync(http, HttpMethod.Get, "/v1/policy", null, cancellationToken);

    public Task<ControlPlaneResponse> PolicyHistoryAsync(CancellationToken cancellationToken) => ControlPlaneHttp.SendAsync(http, HttpMethod.Get, "/v1/policy/history", null, cancellationToken);

    public Task<ControlPlaneResponse> PublishPolicyAsync(JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken) =>
        ControlPlaneHttp.SendAsync(http, HttpMethod.Put, "/admin/policy", new { document, expectedVersion, author }, cancellationToken);

    public Task<ControlPlaneResponse> ControlsAsync(CancellationToken cancellationToken) => ControlPlaneHttp.SendAsync(http, HttpMethod.Get, "/admin/controls", null, cancellationToken);

    public Task<ControlPlaneResponse> SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken) =>
        ControlPlaneHttp.SendAsync(http, HttpMethod.Post, "/admin/controls/emergency-stop", new { enabled = engaged, actor }, cancellationToken);

    public Task<ControlPlaneResponse> SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken) =>
        ControlPlaneHttp.SendAsync(http, HttpMethod.Post, $"/admin/controls/actions/{Uri.EscapeDataString(actionKind)}", new { enabled = disabled, actor }, cancellationToken);
}
