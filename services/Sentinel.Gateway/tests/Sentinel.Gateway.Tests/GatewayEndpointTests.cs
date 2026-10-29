using System.Net;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Application.Postmortems;
using Shouldly;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Api.Identity;
using Sentinel.Gateway.Application.Incidents;

namespace Sentinel.Gateway.Tests;

/// <summary>
/// The Gateway's HTTP surface beyond approvals, as the web app and operators see it: incidents and
/// their pages, postmortems, analytics, the control plane with its audit, agent replays, the caller and
/// the live event stream (Phases 07 and 08). Routes, status codes and bodies are pinned here.
/// </summary>
public sealed class GatewayEndpointTests(GatewayFixture gateway) : IClassFixture<GatewayFixture>
{
    private const string Responder = "responder@example.test";
    private const string Viewer = "viewer@example.test";
    private const string Operator = "operator@example.test";

    private readonly HttpClient _client = gateway.Client;
    private readonly GatewayPeers _peers = gateway.Host.Peers;

    [Fact]
    public async Task Incidents_are_listed_only_for_the_callers_tenants()
    {
        IncidentDetail acme = gateway.Host.Runtime.Add("acme");
        IncidentDetail globex = gateway.Host.Runtime.Add("globex");

        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/v1/incidents?limit=200");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string[] ids = [.. (await BodyAsync(response)).EnumerateArray().Select(incident => incident.GetProperty("incidentId").GetString()!)];
        ids.ShouldContain(acme.Summary.IncidentId.ToString());
        ids.ShouldNotContain(globex.Summary.IncidentId.ToString());
    }

    [Fact]
    public async Task An_incident_and_its_timeline_and_evidence_pages_are_read()
    {
        IncidentDetail incident = gateway.Host.Runtime.Add("acme");
        string path = $"/api/v1/incidents/{incident.Summary.IncidentId}";

        using HttpResponseMessage detail = await SendAsync(HttpMethod.Get, path);
        using HttpResponseMessage timeline = await SendAsync(HttpMethod.Get, $"{path}/timeline?limit=10");
        using HttpResponseMessage evidence = await SendAsync(HttpMethod.Get, $"{path}/evidence?offset=0&limit=10");
        using HttpResponseMessage other = await SendAsync(HttpMethod.Get, $"/api/v1/incidents/{gateway.Host.Runtime.Add("globex").Summary.IncidentId}");

        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(detail)).GetProperty("summary").GetProperty("incidentId").GetString().ShouldBe(incident.Summary.IncidentId.ToString());
        JsonElement page = await BodyAsync(timeline);
        Names(page).ShouldBe(["items", "total"]);
        page.GetProperty("total").GetInt32().ShouldBe(0);
        Names(await BodyAsync(evidence)).ShouldBe(["items", "total"]);
        await ShouldBePlainNotFoundAsync(other);
    }

    [Fact]
    public async Task What_runtime_and_agent_worker_hold_about_an_incident_passes_through_or_is_not_found()
    {
        IncidentDetail incident = gateway.Host.Runtime.Add("acme");
        IncidentDetail remembered = gateway.Host.Runtime.Add("acme");
        _peers.Similar[remembered.Summary.IncidentId] = [];
        _peers.IncidentExecutions[incident.Summary.IncidentId] = """[{"agentExecutionId":"00000000-0000-0000-0000-000000000001","investigator":"model"}]""";
        string path = $"/api/v1/incidents/{incident.Summary.IncidentId}";

        using HttpResponseMessage runRecord = await SendAsync(HttpMethod.Get, $"{path}/run-record");
        using HttpResponseMessage unknownSimilar = await SendAsync(HttpMethod.Get, $"{path}/similar");
        using HttpResponseMessage similar = await SendAsync(HttpMethod.Get, $"/api/v1/incidents/{remembered.Summary.IncidentId}/similar");
        using HttpResponseMessage replay = await SendAsync(HttpMethod.Post, $"{path}/replays/domain");
        using HttpResponseMessage executions = await SendAsync(HttpMethod.Get, $"{path}/agent-executions");

        await ShouldBePlainNotFoundAsync(runRecord);
        await ShouldBePlainNotFoundAsync(unknownSimilar);
        similar.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(similar)).GetArrayLength().ShouldBe(0);
        await ShouldBePlainNotFoundAsync(replay);
        executions.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(executions)).EnumerateArray().Single().GetProperty("investigator").GetString().ShouldBe("model");
    }

    [Fact]
    public async Task A_postmortem_carries_its_narrative_and_edits_are_versioned()
    {
        IncidentDetail incident = gateway.Host.Runtime.Add("acme");
        IncidentDetail unwritten = gateway.Host.Runtime.Add("acme");
        _peers.Postmortems[incident.Summary.IncidentId] = GeneratedPostmortem(incident.Summary.IncidentId);
        string path = $"/api/v1/incidents/{incident.Summary.IncidentId}/postmortem";

        using HttpResponseMessage generated = await SendAsync(HttpMethod.Get, path);
        using HttpResponseMessage saved = await SendAsync(HttpMethod.Put, $"{path}/narrative", new { markdown = "# What happened", expectedVersion = 0 });
        using HttpResponseMessage stale = await SendAsync(HttpMethod.Put, $"{path}/narrative", new { markdown = "# Overwrite", expectedVersion = 0 });
        using HttpResponseMessage tooLong = await SendAsync(HttpMethod.Put, $"{path}/narrative", new { markdown = new string('x', 200_001), expectedVersion = 1 });
        using HttpResponseMessage missing = await SendAsync(HttpMethod.Put, $"/api/v1/incidents/{unwritten.Summary.IncidentId}/postmortem/narrative", new { markdown = "# Text", expectedVersion = 0 });
        using HttpResponseMessage noPostmortem = await SendAsync(HttpMethod.Get, $"/api/v1/incidents/{unwritten.Summary.IncidentId}/postmortem");
        using HttpResponseMessage reread = await SendAsync(HttpMethod.Get, path);

        generated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(generated)).TryGetProperty("narrative", out _).ShouldBeFalse();
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement narrative = await BodyAsync(saved);
        Names(narrative).ShouldBe(["markdown", "version", "editedBy", "editedAt"]);
        narrative.GetProperty("version").GetInt32().ShouldBe(1);
        narrative.GetProperty("editedBy").GetString().ShouldBe(Responder);
        await ShouldBeProblemAsync(stale, HttpStatusCode.Conflict, "Postmortem.VersionConflict");
        await ShouldBeProblemAsync(tooLong, HttpStatusCode.UnprocessableEntity, "Postmortem.TooLong");
        await ShouldBeProblemAsync(missing, HttpStatusCode.NotFound, "Postmortem.NotFound");
        await ShouldBePlainNotFoundAsync(noPostmortem);
        (await BodyAsync(reread)).GetProperty("narrative").GetProperty("markdown").GetString().ShouldBe("# What happened");
    }

    [Fact]
    public async Task Analytics_events_are_accepted_from_the_catalog_and_counted_for_operators()
    {
        using HttpResponseMessage accepted = await SendAsync(HttpMethod.Post, "/api/v1/analytics", new { name = "admin.viewed", properties = new Dictionary<string, string> { ["area"] = "policy" } });
        using HttpResponseMessage unknown = await SendAsync(HttpMethod.Post, "/api/v1/analytics", new { name = "user.tracked" });
        using HttpResponseMessage counts = await SendAsync(HttpMethod.Get, "/api/v1/admin/analytics?days=1", user: Operator);
        using HttpResponseMessage forbidden = await SendAsync(HttpMethod.Get, "/api/v1/admin/analytics", user: Viewer);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await accepted.Content.ReadAsStringAsync()).ShouldBeEmpty();
        await ShouldBeProblemAsync(unknown, HttpStatusCode.UnprocessableEntity, "Analytics.UnknownEvent");
        counts.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement count = (await BodyAsync(counts)).EnumerateArray().First(entry => entry.GetProperty("event").GetString() == "admin.viewed");
        Names(count).ShouldBe(["day", "event", "dimensions", "count"]);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Executor_policy_and_controls_pass_through_and_every_change_is_audited()
    {
        string version = $"policy-{Guid.NewGuid():N}";

        using HttpResponseMessage policy = await SendAsync(HttpMethod.Get, "/api/v1/admin/policy", user: Operator);
        using HttpResponseMessage history = await SendAsync(HttpMethod.Get, "/api/v1/admin/policy/history", user: Operator);
        using HttpResponseMessage publish = await SendAsync(HttpMethod.Put, "/api/v1/admin/policy", new { document = new { version }, expectedVersion = "policy-1" }, Operator);
        using HttpResponseMessage controls = await SendAsync(HttpMethod.Get, "/api/v1/admin/controls", user: Operator);
        using HttpResponseMessage stop = await SendAsync(HttpMethod.Post, "/api/v1/admin/controls/emergency-stop", new { enabled = true }, Operator);
        using HttpResponseMessage disable = await SendAsync(HttpMethod.Post, "/api/v1/admin/controls/actions/rollback", new { enabled = true }, Operator);
        using HttpResponseMessage sources = await SendAsync(HttpMethod.Get, "/api/v1/admin/sources", user: Operator);
        using HttpResponseMessage audit = await SendAsync(HttpMethod.Get, "/api/v1/admin/audit?limit=500", user: Operator);
        using HttpResponseMessage forbidden = await SendAsync(HttpMethod.Get, "/api/v1/admin/policy", user: Responder);

        policy.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(policy)).GetProperty("version").GetString().ShouldBe("policy-1");
        history.StatusCode.ShouldBe(HttpStatusCode.OK);
        publish.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement refused = await BodyAsync(publish);
        refused.GetProperty("detail").GetString().ShouldBe("The policy changed since it was read.");
        refused.TryGetProperty("code", out _).ShouldBeFalse();
        (await BodyAsync(controls)).GetProperty("emergencyStop").GetBoolean().ShouldBeFalse();
        stop.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await BodyAsync(sources)).EnumerateArray().Single().GetProperty("sourceId").GetString().ShouldBe("prometheus:slo_burn_rate");
        audit.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement[] entries = [.. (await BodyAsync(audit)).EnumerateArray()];
        JsonElement published = entries.Single(entry => entry.GetProperty("target").GetString() == $"policy/{version}");
        Names(published).ShouldBe(["sequence", "actor", "action", "target", "detail", "occurredAt"]);
        published.GetProperty("action").GetString().ShouldBe("policy.publish");
        published.GetProperty("actor").GetString().ShouldBe(Operator);
        entries.ShouldContain(entry => entry.GetProperty("action").GetString() == "controls.emergency-stop.engage");
        entries.ShouldContain(entry => entry.GetProperty("target").GetString() == "executor/actions/rollback");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Configuration_is_read_and_published_at_the_area_that_owns_it_and_audited()
    {
        string version = $"models-{Guid.NewGuid():N}";

        using HttpResponseMessage areas = await SendAsync(HttpMethod.Get, "/api/v1/admin/configuration", user: Operator);
        using HttpResponseMessage current = await SendAsync(HttpMethod.Get, "/api/v1/admin/configuration/runtime", user: Operator);
        using HttpResponseMessage history = await SendAsync(HttpMethod.Get, "/api/v1/admin/configuration/runtime/history", user: Operator);
        using HttpResponseMessage unknown = await SendAsync(HttpMethod.Get, "/api/v1/admin/configuration/secrets", user: Operator);
        using HttpResponseMessage publish = await SendAsync(HttpMethod.Put, "/api/v1/admin/configuration/models", new { document = new { version }, expectedVersion = "deployment" }, Operator);
        using HttpResponseMessage audit = await SendAsync(HttpMethod.Get, "/api/v1/admin/audit?limit=500", user: Operator);

        (await BodyAsync(areas)).EnumerateArray().Select(area => area.GetString()).ShouldBe(["runtime", "models", "sources", "api"]);
        (await BodyAsync(current)).GetProperty("version").GetString().ShouldBe("deployment");
        history.StatusCode.ShouldBe(HttpStatusCode.OK);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BodyAsync(unknown)).GetProperty("detail").GetString().ShouldBe("The configuration areas are runtime, models, sources, api.");
        publish.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await BodyAsync(publish)).GetProperty("detail").GetString().ShouldBe("The configuration does not satisfy the owner's rules.");
        _peers.Published.ShouldContain($"models:deployment:{Operator}");
        (await BodyAsync(audit)).EnumerateArray().ShouldContain(entry => entry.GetProperty("target").GetString() == $"models/{version}" && entry.GetProperty("action").GetString() == "configuration.publish");
    }

    [Fact]
    public async Task An_agent_replay_is_found_only_in_the_callers_tenant_and_passes_through()
    {
        var foreign = AgentExecutionId.New();
        var own = AgentExecutionId.New();
        _peers.Executions[foreign] = """{"tenantId":"globex"}""";
        _peers.Executions[own] = """{"tenantId":"acme"}""";

        using HttpResponseMessage hidden = await SendAsync(HttpMethod.Post, $"/api/v1/agent-executions/{foreign}/replays", new { level = "investigation" });
        using HttpResponseMessage refused = await SendAsync(HttpMethod.Post, $"/api/v1/agent-executions/{own}/replays", new { level = "investigation" });

        await ShouldBePlainNotFoundAsync(hidden);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BodyAsync(refused)).GetProperty("detail").GetString().ShouldBe("AgentWorker refused the replay.");
    }

    [Fact]
    public async Task The_caller_sees_its_identity_roles_and_tenants()
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/v1/me", user: Operator);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement me = await BodyAsync(response);
        Names(me).ShouldBe(["id", "roles", "tenants"]);
        me.GetProperty("id").GetString().ShouldBe(Operator);
        me.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).ShouldBe(["responder", "operator"], ignoreOrder: true);
        me.GetProperty("tenants").EnumerateArray().Select(tenant => tenant.GetString()).ShouldBe(["acme"]);
    }

    [Fact]
    public async Task The_live_stream_sends_the_callers_tenants_events_with_their_sequence()
    {
        var incident = IncidentId.New();
        long before;
        await using (AsyncServiceScope scope = gateway.Host.Services.CreateAsyncScope())
        {
            ILiveEventStore events = scope.ServiceProvider.GetRequiredService<ILiveEventStore>();
            before = await events.LatestAsync(CancellationToken.None);
            await events.AppendAsync(new LiveEventDraft(TenantId.From("globex"), EnvironmentId.From("reference"), IncidentId.New(), LiveEventKinds.IncidentOpened, 1, "Another tenant's incident.", Severity.High, GatewayHost.Now), CancellationToken.None);
            await events.AppendAsync(new LiveEventDraft(TenantId.From("acme"), EnvironmentId.From("reference"), incident, LiveEventKinds.IncidentOpened, 1, "checkout burns its SLO.", Severity.Critical, GatewayHost.Now), CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
        }

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/v1/events?after={before}", UriKind.Relative));
        request.Headers.Add("X-Sentinel-User", Responder);
        using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(stop.Token));
        var frame = new List<string>();
        while (await reader.ReadLineAsync(stop.Token) is { } line && (line.Length > 0 || frame.Count == 0))
        {
            if (line.Length > 0)
            {
                frame.Add(line);
            }
        }

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        frame.ShouldContain("event: incident");
        frame.ShouldContain(line => line.StartsWith("id: ", StringComparison.Ordinal));
        using var data = JsonDocument.Parse(frame.Single(line => line.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..]);
        data.RootElement.GetProperty("incidentId").GetString().ShouldBe(incident.ToString());
        data.RootElement.GetProperty("tenantId").GetString().ShouldBe("acme");
        data.RootElement.GetProperty("kind").GetString().ShouldBe(LiveEventKinds.IncidentOpened);
    }

    private static Postmortem GeneratedPostmortem(IncidentId incidentId) => new()
    {
        IncidentId = incidentId,
        Title = "checkout-service slo_burn_rate at 9.0",
        Status = IncidentStatus.Resolved,
        StreamVersion = 22,
        GeneratedAt = GatewayHost.Now,
        Summary = new PostmortemFact("Resolved.", [new SourceRef("event", "1")]),
        Versions = new VersionManifest { SchemaVersion = 1 }
    };

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string user = Responder)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-Sentinel-User", user);
        if (method != HttpMethod.Get)
        {
            // Every mutation of the public API carries one (Phase 08); analytics ignores it.
            request.Headers.Add("Idempotency-Key", $"test-{Guid.NewGuid():N}");
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        JsonElement problem = await BodyAsync(response);
        problem.GetProperty("title").GetString().ShouldBe(code);
        problem.GetProperty("code").GetString().ShouldBe(code);
    }

    private static async Task ShouldBePlainNotFoundAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        JsonElement problem = await BodyAsync(response);
        problem.GetProperty("title").GetString().ShouldBe("Not Found");
        problem.TryGetProperty("code", out _).ShouldBeFalse();
    }
}
