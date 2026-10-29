using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using Sentinel.Gateway.Application.Analytics;
using Sentinel.Gateway.Application.Approvals;
using Sentinel.Gateway.Application.Incidents;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Application.Postmortems;
using Sentinel.Gateway.Api;
using Sentinel.Gateway.Infrastructure;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddBuildingBlocks(Sentinel.Gateway.Application.AssemblyReference.Assembly);

// Runtime's lifecycle events feed the live-update channel and notifications (Phase 08).
builder.AddSentinelMessaging("api")
    .Produces<RecordApproval>()
    .Produces<RecordRejection>()
    .Produces<RecordRevocation>()
    .Consumes<IncidentOpened, IncidentOpenedHandler>()
    .Consumes<IncidentStatusChanged, IncidentStatusChangedHandler>()
    .Consumes<IncidentBlackboardUpdated, IncidentBlackboardUpdatedHandler>()
    .Consumes<DecisionRecorded, DecisionRecordedHandler>()
    .Consumes<RemediationPlanProposed, RemediationPlanProposedHandler>()
    .Consumes<RecoveryEvaluated, RecoveryEvaluatedHandler>();
builder.Services.AddApiInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.AddSentinelIdentity();

await using WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseIdempotencyKeys();

app.MapDefaultEndpoints();
app.MapOperatorEndpoints();
app.MapOpenApi();

// Approval verification keys, published for Executor (ADR-006). Public keys only.
app.MapGet(ApprovalAttestation.KeysPath, async (IApprovalSigner signer, CancellationToken cancellationToken) =>
    await signer.PublishedKeysAsync(cancellationToken));

RouteGroupBuilder api = app.MapGroup("/api/v1").RequireAuthorization(Roles.Responder);

api.MapGet("/me", (ClaimsPrincipal user) => new
{
    Id = user.FindFirstValue(ClaimTypes.NameIdentifier),
    Roles = user.FindAll(ClaimTypes.Role).Select(role => role.Value),
    Tenants = user.Tenants()
});

api.MapGet("/incidents", async (int? limit, ClaimsPrincipal user, IRuntimeIncidents incidents, CancellationToken cancellationToken) =>
    (await incidents.ListAsync(Math.Clamp(limit ?? 50, 1, 200), cancellationToken)).Where(incident => user.CanAccess(incident.TenantId)));

// Live updates (Phase 08): server-sent events for the caller's tenants, resumable from Last-Event-ID. Each event
// names what changed; the query endpoints stay the source of truth.
api.MapGet("/events", (HttpContext http, long? after, ClaimsPrincipal user, ILiveEventStore events, CancellationToken cancellationToken) =>
{
    long? resume = long.TryParse(http.Request.Headers["Last-Event-ID"].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out long lastEventId) ? lastEventId : after;
    return TypedResults.ServerSentEvents(LiveEventStream.ReadAsync(events, user.Tenants(), resume, cancellationToken));
});

// Every incident endpoint answers only within the caller's tenants.
RouteGroupBuilder incident = api.MapGroup("/incidents/{incidentId:guid}").AddEndpointFilter<IncidentTenantFilter>();

incident.MapGet(string.Empty, async (Guid incidentId, IRuntimeIncidents incidents, CancellationToken cancellationToken) =>
    await incidents.GetAsync(IncidentId.From(incidentId), cancellationToken) is { } detail ? Results.Ok(detail) : Results.NotFound());

// Long timelines and evidence sets, one page at a time (Phase 08 API principles).
incident.MapGet("/timeline", async (Guid incidentId, long? before, int? limit, IRuntimeIncidents incidents, CancellationToken cancellationToken) =>
    await incidents.GetAsync(IncidentId.From(incidentId), cancellationToken) is { } detail ? Results.Ok(Pages.Timeline(detail.Timeline ?? [], before, limit)) : Results.NotFound());
incident.MapGet("/evidence", async (Guid incidentId, int? offset, int? limit, IRuntimeIncidents incidents, CancellationToken cancellationToken) =>
    await incidents.GetAsync(IncidentId.From(incidentId), cancellationToken) is { } detail ? Results.Ok(Pages.Evidence(detail.Evidence ?? [], offset, limit)) : Results.NotFound());

incident.MapGet("/run-record", async (Guid incidentId, IRuntimeIncidents incidents, CancellationToken cancellationToken) =>
    await incidents.RunRecordAsync(IncidentId.From(incidentId), cancellationToken) is { } record ? Results.Ok(record) : Results.NotFound());

incident.MapPost("/plans/{planId:guid}/approve", async (Guid incidentId, Guid planId, ApprovePlanRequest request, ClaimsPrincipal user, ApprovalService approvals, CancellationToken cancellationToken) =>
    {
        Result<ApprovalReceipt> result = await approvals.ApproveAsync(
            new ApprovePlan(IncidentId.From(incidentId), PlanId.From(planId), request.PlanVersion, request.PlanHash, user.FindFirstValue(ClaimTypes.NameIdentifier)!),
            cancellationToken);
        return result.IsSuccess ? Results.Accepted($"/api/v1/incidents/{incidentId}", result.Value) : result.Error.ToProblem();
    })
    .RequireAuthorization(Roles.Approver);

incident.MapPost("/plans/{planId:guid}/reject", async (Guid incidentId, Guid planId, RejectPlanRequest request, ClaimsPrincipal user, ApprovalService approvals, CancellationToken cancellationToken) =>
    {
        Result<ApprovalReceipt> result = await approvals.RejectAsync(
            new RejectPlan(IncidentId.From(incidentId), PlanId.From(planId), request.PlanVersion, request.Reason, user.FindFirstValue(ClaimTypes.NameIdentifier)!),
            cancellationToken);
        return result.IsSuccess ? Results.Accepted($"/api/v1/incidents/{incidentId}", result.Value) : result.Error.ToProblem();
    })
    .RequireAuthorization(Roles.Approver);

incident.MapPost("/plans/{planId:guid}/revoke", async (Guid incidentId, Guid planId, RejectPlanRequest request, ClaimsPrincipal user, ApprovalService approvals, CancellationToken cancellationToken) =>
    {
        Result<ApprovalReceipt> result = await approvals.RevokeAsync(
            new RevokeApproval(IncidentId.From(incidentId), PlanId.From(planId), request.PlanVersion, request.Reason, user.FindFirstValue(ClaimTypes.NameIdentifier)!),
            cancellationToken);
        return result.IsSuccess ? Results.Accepted($"/api/v1/incidents/{incidentId}", result.Value) : result.Error.ToProblem();
    })
    .RequireAuthorization(Roles.Approver);

// Phase 07: postmortems with their narrative, similar incidents, agent executions and replays.
incident.MapGet("/postmortem", async (Guid incidentId, PostmortemService postmortems, CancellationToken cancellationToken) =>
    await postmortems.GetAsync(IncidentId.From(incidentId), cancellationToken) is { } postmortem ? Results.Ok(postmortem) : Results.NotFound());

incident.MapPut("/postmortem/narrative", async (Guid incidentId, SaveNarrativeRequest request, ClaimsPrincipal user, PostmortemService postmortems, CancellationToken cancellationToken) =>
{
    Result<PostmortemNarrative> result = await postmortems.SaveNarrativeAsync(
        new SaveNarrative(IncidentId.From(incidentId), request.Markdown, request.ExpectedVersion, user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        cancellationToken);
    return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
});

incident.MapGet("/similar", async (Guid incidentId, IRuntimeHistory history, CancellationToken cancellationToken) =>
    await history.SimilarAsync(IncidentId.From(incidentId), cancellationToken) is { } similar ? Results.Ok(similar) : Results.NotFound());

incident.MapPost("/replays/domain", async (Guid incidentId, IRuntimeHistory history, CancellationToken cancellationToken) =>
    await history.ReplayAsync(IncidentId.From(incidentId), cancellationToken) is { } replay ? Results.Ok(replay) : Results.NotFound());

incident.MapGet("/agent-executions", async (Guid incidentId, IAgentExecutions executions, CancellationToken cancellationToken) =>
    Results.Ok(await executions.ForIncidentAsync(IncidentId.From(incidentId), cancellationToken)));

api.MapPost("/agent-executions/{executionId:guid}/replays", async (Guid executionId, InvestigationReplayRequest request, ClaimsPrincipal user, IAgentExecutions executions, CancellationToken cancellationToken) =>
{
    var id = AgentExecutionId.From(executionId);
    JsonElement? execution = await executions.GetAsync(id, cancellationToken);
    if (execution is not { } found || !found.TryGetProperty("tenantId", out JsonElement tenant) || !user.CanAccess(TenantId.From(tenant.GetString()!)))
    {
        return Results.NotFound();
    }

    AgentReplayResponse response = await executions.ReplayAsync(id, request, cancellationToken);
    return response.Result is { } result ? Results.Ok(result) : Results.Problem(response.Problem, statusCode: response.StatusCode);
});

// The control plane (Phase 08): operators change policy and controls; the owning service validates, Api audits.
// Product analytics (Phase 08): approved events only, counted per day without who, which incident or which tenant.
api.MapPost("/analytics", async (AnalyticsEvent analyticsEvent, ProductAnalyticsService analytics, CancellationToken cancellationToken) =>
{
    Result recorded = await analytics.RecordAsync(analyticsEvent, cancellationToken);
    return recorded.IsSuccess ? Results.Accepted() : recorded.Error.ToProblem();
});

RouteGroupBuilder admin = api.MapGroup("/admin").RequireAuthorization(Roles.Operator);
admin.MapGet("/analytics", async (int? days, ProductAnalyticsService analytics, CancellationToken cancellationToken) =>
    await analytics.RecentAsync(days ?? 30, cancellationToken));

admin.MapGet("/policy", async (IExecutorControlPlane executor, CancellationToken cancellationToken) => ControlPlaneResult.From(await executor.PolicyAsync(cancellationToken)));
admin.MapGet("/policy/history", async (IExecutorControlPlane executor, CancellationToken cancellationToken) => ControlPlaneResult.From(await executor.PolicyHistoryAsync(cancellationToken)));
admin.MapPut("/policy", async (PublishPolicyRequest request, ClaimsPrincipal user, ControlPlaneService controlPlane, CancellationToken cancellationToken) =>
    ControlPlaneResult.From(await controlPlane.PublishPolicyAsync(request.Document, request.ExpectedVersion, user.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)));
admin.MapGet("/controls", async (IExecutorControlPlane executor, CancellationToken cancellationToken) => ControlPlaneResult.From(await executor.ControlsAsync(cancellationToken)));
admin.MapPost("/controls/emergency-stop", async (ControlRequest request, ClaimsPrincipal user, ControlPlaneService controlPlane, CancellationToken cancellationToken) =>
    ControlPlaneResult.From(await controlPlane.SetEmergencyStopAsync(request.Enabled, user.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)));
admin.MapPost("/controls/actions/{actionKind}", async (string actionKind, ControlRequest request, ClaimsPrincipal user, ControlPlaneService controlPlane, CancellationToken cancellationToken) =>
    ControlPlaneResult.From(await controlPlane.SetActionDisabledAsync(actionKind, request.Enabled, user.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)));
admin.MapGet("/sources", async (ISourceHealth sources, CancellationToken cancellationToken) => ControlPlaneResult.From(await sources.SourcesAsync(cancellationToken)));

// Configuration areas (Phase 08): SLOs, catalog and workflow (runtime), models, provider sources, tenants and notifications (api).
admin.MapGet("/configuration", () => ConfigurationAreas.All);
admin.MapGet("/configuration/{area}", async (string area, ControlPlaneService controlPlane, CancellationToken cancellationToken) =>
    ControlPlaneResult.From(await controlPlane.ConfigurationAsync(area, cancellationToken)));
admin.MapGet("/configuration/{area}/history", async (string area, ControlPlaneService controlPlane, CancellationToken cancellationToken) =>
    ControlPlaneResult.From(await controlPlane.ConfigurationHistoryAsync(area, cancellationToken)));
admin.MapPut("/configuration/{area}", async (string area, PublishConfigurationRequest request, ClaimsPrincipal user, ControlPlaneService controlPlane, CancellationToken cancellationToken) =>
    ControlPlaneResult.From(await controlPlane.PublishConfigurationAsync(area, request.Document, request.ExpectedVersion, user.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)));
admin.MapGet("/audit", async (int? limit, IAuditLog audit, CancellationToken cancellationToken) => await audit.RecentAsync(Math.Clamp(limit ?? 100, 1, 500), cancellationToken));

await app.RunAsync();

public partial class Program;

namespace Sentinel.Gateway.Api
{
    /// <summary>Live events after a sequence, then new ones as they are recorded, until the client disconnects.</summary>
    public static class LiveEventStream
    {
        private const int BatchSize = 100;
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        public static async IAsyncEnumerable<SseItem<LiveEvent>> ReadAsync(ILiveEventStore events, IReadOnlyList<TenantId> tenants, long? after, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            long position = after ?? await events.LatestAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                IReadOnlyList<LiveEvent> batch = await events.AfterAsync(position, tenants, BatchSize, cancellationToken);
                foreach (LiveEvent live in batch)
                {
                    position = live.Sequence;
                    yield return new SseItem<LiveEvent>(live, "incident") { EventId = live.Sequence.ToString(CultureInfo.InvariantCulture) };
                }

                if (batch.Count < BatchSize)
                {
                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }
        }
    }

    /// <summary>Passes a control-plane answer through with its status code and problem.</summary>
    internal static class ControlPlaneResult
    {
        public static IResult From(ControlPlaneResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);

            if (response.IsSuccess)
            {
                return response.Body is { } body ? Results.Json(body, statusCode: response.StatusCode) : Results.StatusCode(response.StatusCode);
            }

            return Results.Problem(response.Problem, statusCode: response.StatusCode);
        }
    }
}
