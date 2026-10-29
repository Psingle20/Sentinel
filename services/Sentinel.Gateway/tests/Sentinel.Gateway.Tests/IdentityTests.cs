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

/// <summary>Phase 08 identity: JWT bearer tokens from the configured authority, with roles and tenants mapped to what every endpoint reads.</summary>
public sealed class IdentityTests : IAsyncLifetime
{
    private const string Issuer = "https://identity.example.test";
    private const string Audience = "sentinel-api";

    private readonly RSA _signingKey = RSA.Create(2048);
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration["Gateway:Identity:Audience"] = Audience;
        builder.Services.AddSentinelIdentity(builder.Configuration, builder.Environment);
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            // The test plays the authority: tokens are signed with a key generated for this run.
            options.Authority = null;
            options.TokenValidationParameters.ValidIssuer = Issuer;
            options.TokenValidationParameters.ValidAudience = Audience;
            options.TokenValidationParameters.IssuerSigningKey = new RsaSecurityKey(_signingKey);
        });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapGet("/whoami", (ClaimsPrincipal user) => new { id = user.FindFirstValue(ClaimTypes.NameIdentifier), tenants = user.Tenants().Select(tenant => tenant.Value) }).RequireAuthorization(new AuthorizeAttribute { Roles = Roles.Responder });
        _app.MapGet("/approve", () => "ok").RequireAuthorization(new AuthorizeAttribute { Roles = Roles.Approver });
        _app.MapGet("/admin", () => "ok").RequireAuthorization(new AuthorizeAttribute { Roles = Roles.Operator });
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        _signingKey.Dispose();
    }

    [Fact]
    public async Task A_valid_token_carries_its_subject_roles_and_tenants()
    {
        using HttpResponseMessage response = await SendAsync("/whoami", Token("responder@example.test", ["responder"], ["acme", "globex"]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("id").GetString().ShouldBe("responder@example.test");
        body.RootElement.GetProperty("tenants").EnumerateArray().Select(tenant => tenant.GetString()).ShouldBe(["acme", "globex"]);
    }

    [Fact]
    public async Task Each_role_is_required_where_it_is_named()
    {
        string responder = Token("viewer@example.test", ["responder"], ["acme"]);

        (await SendAsync("/approve", responder)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync("/admin", responder)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync("/admin", Token("operator@example.test", ["responder", "operator"], ["acme"]))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task No_token_a_foreign_key_or_the_wrong_audience_is_refused()
    {
        (await SendAsync("/whoami", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var foreign = RSA.Create(2048);
        (await SendAsync("/whoami", Token("responder@example.test", ["responder"], ["acme"], foreign))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync("/whoami", Token("responder@example.test", ["responder"], ["acme"], audience: "another-api"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> SendAsync(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    private string Token(string subject, string[] roles, string[] tenants, RSA? key = null, string audience = Audience) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key ?? _signingKey), SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["roles"] = roles,
                ["sentinel_tenant"] = tenants
            }
        });
}
