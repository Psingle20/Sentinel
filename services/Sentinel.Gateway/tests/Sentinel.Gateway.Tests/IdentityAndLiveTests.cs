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

public sealed class LiveUpdateTests
{
    private static readonly TenantId Acme = TenantId.From("acme");
    private static readonly TenantId Globex = TenantId.From("globex");

    [Fact]
    public async Task A_reconnecting_client_resumes_after_its_last_event_and_sees_only_its_tenants()
    {
        var store = new InMemoryLiveEvents();
        await store.AppendAsync(Draft(Acme, "incident-opened"), CancellationToken.None);
        await store.AppendAsync(Draft(Globex, "incident-opened"), CancellationToken.None);
        await store.AppendAsync(Draft(Acme, "decision-recorded"), CancellationToken.None);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var received = new List<LiveEvent>();
        IAsyncEnumerable<LiveEvent> stream = (await new StreamLiveEventsQueryHandler(store).Handle(new StreamLiveEventsQuery([Acme], 1), stop.Token)).Value;
        await foreach (LiveEvent item in stream)
        {
            received.Add(item);
            if (received.Count == 1)
            {
                break;
            }
        }

        LiveEvent only = received.ShouldHaveSingleItem();
        only.Kind.ShouldBe("decision-recorded");
        only.Sequence.ShouldBe(3);
    }

    [Fact]
    public void Routes_pick_channels_by_event_and_severity_and_ignore_unknown_channels()
    {
        var options = new NotificationOptions { WebBaseAddress = new Uri("http://web.example.test") };
        options.Channels.Add(new NotificationChannelOptions { Name = "oncall", Kind = "webhook", Url = new Uri("https://hooks.example.test/sentinel") });
        options.Routes.Add(new NotificationRouteOptions { Event = "incident-opened", MinimumSeverity = Severity.High, Channel = "oncall" });
        options.Routes.Add(new NotificationRouteOptions { Event = "plan-proposed", Channel = "oncall" });
        options.Routes.Add(new NotificationRouteOptions { Event = "plan-proposed", Channel = "not-configured" });

        NotificationRouter.Route(Draft(Acme, "incident-opened", Severity.Critical), options, Guid.NewGuid).ShouldHaveSingleItem().Link!.ToString().ShouldStartWith("http://web.example.test/incidents/");
        NotificationRouter.Route(Draft(Acme, "incident-opened", Severity.Low), options, Guid.NewGuid).ShouldBeEmpty();
        NotificationRouter.Route(Draft(Acme, "plan-proposed"), options, Guid.NewGuid).ShouldHaveSingleItem().Channel.ShouldBe("oncall");
        NotificationRouter.Route(Draft(Acme, "evidence-updated"), options, Guid.NewGuid).ShouldBeEmpty();
    }

    [Fact]
    public void An_escalation_is_its_own_event_so_it_can_be_routed()
    {
        LiveEventDraft draft = LiveEventMapper.From(new IncidentStatusChanged
        {
            TenantId = Acme,
            EnvironmentId = EnvironmentId.From("reference"),
            IncidentId = IncidentId.New(),
            Status = IncidentStatus.Escalated,
            Reason = "Abstained.",
            ChangedAt = DateTimeOffset.UnixEpoch,
            StreamVersion = 9
        });

        draft.Kind.ShouldBe(LiveEventKinds.IncidentEscalated);
        draft.Summary.ShouldContain("Abstained.");
    }

    private static LiveEventDraft Draft(TenantId tenant, string kind, Severity? severity = null) =>
        new(tenant, EnvironmentId.From("reference"), IncidentId.New(), kind, 1, kind, severity, DateTimeOffset.UnixEpoch);

    private sealed class InMemoryLiveEvents : ILiveEventStore
    {
        private readonly List<LiveEvent> _events = [];

        public Task AppendAsync(LiveEventDraft draft, CancellationToken cancellationToken)
        {
            _events.Add(new LiveEvent(_events.Count + 1, draft.TenantId, draft.EnvironmentId, draft.IncidentId, draft.Kind, draft.StreamVersion, draft.Summary, draft.Severity, draft.OccurredAt));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LiveEvent>> AfterAsync(long after, IReadOnlyCollection<TenantId> tenants, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LiveEvent>>([.. _events.Where(live => live.Sequence > after && tenants.Contains(live.TenantId)).Take(limit)]);

        public Task<long> LatestAsync(CancellationToken cancellationToken) => Task.FromResult((long)_events.Count);
    }
}

public sealed class ControlPlaneTests
{
    // The control plane's handlers behind the calls these tests make, composed as the host composes them.
    private sealed class ControlPlane(IExecutorControlPlane executor, IConfigurationOwners owners, IAuditLog audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        private readonly ControlPlaneAudit _audit = new(audit, unitOfWork, time);

        public async Task<ControlPlaneResponse> PublishPolicyAsync(JsonElement document, string expectedVersion, string actor, CancellationToken cancellationToken) =>
            (await new PublishPolicyCommandHandler(executor, _audit).Handle(new PublishPolicyCommand(document, expectedVersion, actor), cancellationToken)).Value;

        public async Task<ControlPlaneResponse> SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken) =>
            (await new SetEmergencyStopCommandHandler(executor, _audit).Handle(new SetEmergencyStopCommand(engaged, actor), cancellationToken)).Value;

        public async Task<ControlPlaneResponse> PublishConfigurationAsync(string area, JsonElement document, string expectedVersion, string actor, CancellationToken cancellationToken) =>
            (await new PublishConfigurationCommandHandler(owners, _audit).Handle(new PublishConfigurationCommand(area, document, expectedVersion, actor), cancellationToken)).Value;

        public async Task<ControlPlaneResponse> ConfigurationAsync(string area, CancellationToken cancellationToken) =>
            (await new GetConfigurationQueryHandler(owners).Handle(new GetConfigurationQuery(area), cancellationToken)).Value;
    }
    [Fact]
    public async Task Every_control_plane_change_is_audited_whether_it_succeeded_or_was_refused()
    {
        var audit = new RecordingAudit();
        var service = new ControlPlane(new RefusingExecutor(), new RecordingOwners(), audit, new NoUnitOfWork(), new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        using var document = JsonDocument.Parse("""{ "version": "policy-2026.10.3", "rules": [] }""");

        ControlPlaneResponse published = await service.PublishPolicyAsync(document.RootElement, "policy-2026.10.2", "operator@example.test", CancellationToken.None);
        await service.SetEmergencyStopAsync(true, "operator@example.test", CancellationToken.None);

        published.IsSuccess.ShouldBeFalse();
        audit.Entries.Select(entry => (entry.Action, entry.Target)).ShouldBe([("policy.publish", "policy/policy-2026.10.3"), ("controls.emergency-stop.engage", "executor/emergency-stop")]);
        audit.Entries.ShouldAllBe(entry => entry.Actor == "operator@example.test");
    }

    [Fact]
    public async Task A_configuration_change_goes_to_the_area_that_owns_it_and_is_audited()
    {
        var audit = new RecordingAudit();
        var owners = new RecordingOwners();
        var service = new ControlPlane(new RefusingExecutor(), owners, audit, new NoUnitOfWork(), new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        using var document = JsonDocument.Parse("""{ "version": "runtime-2026.10.29.1", "detection": [] }""");

        ControlPlaneResponse published = await service.PublishConfigurationAsync(ConfigurationAreas.Runtime, document.RootElement, "deployment", "operator@example.test", CancellationToken.None);

        published.StatusCode.ShouldBe(200);
        owners.Published.ShouldBe([(ConfigurationAreas.Runtime, "deployment", "operator@example.test")]);
        audit.Entries.ShouldBe([("operator@example.test", "configuration.publish", "runtime/runtime-2026.10.29.1")]);
    }

    [Fact]
    public async Task An_unknown_configuration_area_reaches_no_service()
    {
        var owners = new RecordingOwners();
        var service = new ControlPlane(new RefusingExecutor(), owners, new RecordingAudit(), new NoUnitOfWork(), new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        using var document = JsonDocument.Parse("""{ "version": "x" }""");

        (await service.ConfigurationAsync("secrets", CancellationToken.None)).StatusCode.ShouldBe(404);
        (await service.PublishConfigurationAsync("secrets", document.RootElement, "deployment", "operator@example.test", CancellationToken.None)).StatusCode.ShouldBe(404);
        owners.Published.ShouldBeEmpty();
    }

    private sealed class RecordingOwners : IConfigurationOwners
    {
        private static readonly ControlPlaneResponse Done = new(200, null, null);

        public List<(string Area, string ExpectedVersion, string Author)> Published { get; } = [];

        public Task<ControlPlaneResponse> CurrentAsync(string area, CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> HistoryAsync(string area, CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> PublishAsync(string area, JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken)
        {
            Published.Add((area, expectedVersion, author));
            return Task.FromResult(Done);
        }
    }

    private sealed class RefusingExecutor : IExecutorControlPlane
    {
        private static readonly ControlPlaneResponse Refused = new(400, null, "Policy.Invalid: rule covers no environment.");
        private static readonly ControlPlaneResponse Done = new(204, null, null);

        public Task<ControlPlaneResponse> PolicyAsync(CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> PolicyHistoryAsync(CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> PublishPolicyAsync(JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken) => Task.FromResult(Refused);

        public Task<ControlPlaneResponse> ControlsAsync(CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken) => Task.FromResult(Done);
    }

    private sealed class RecordingAudit : IAuditLog
    {
        public List<(string Actor, string Action, string Target)> Entries { get; } = [];

        public Task RecordAsync(string actor, string action, string target, object detail, DateTimeOffset occurredAt, CancellationToken cancellationToken)
        {
            Entries.Add((actor, action, target));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> RecentAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditEntry>>([]);
    }

    private sealed class NoUnitOfWork : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
