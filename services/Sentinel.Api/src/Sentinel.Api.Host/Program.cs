using System.Security.Claims;
using CleanArchitecture.BuildingBlocks;
using Microsoft.AspNetCore.Authentication;
using Sentinel.Api.Application.Approvals;
using Sentinel.Api.Host;
using Sentinel.Api.Infrastructure;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddBuildingBlocks(Sentinel.Api.Application.AssemblyReference.Assembly);
builder.AddSentinelMessaging("api")
    .Produces<RecordApproval>()
    .Produces<RecordRejection>()
    .Produces<RecordRevocation>();
builder.Services.AddApiInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();

builder.Services.Configure<DevelopmentIdentityOptions>(builder.Configuration.GetSection(DevelopmentIdentityOptions.Section));
AuthenticationBuilder authentication = builder.Services.AddAuthentication(DevelopmentIdentityHandler.SchemeName);
if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>($"{DevelopmentIdentityOptions.Section}:Enabled"))
{
    authentication.AddScheme<AuthenticationSchemeOptions, DevelopmentIdentityHandler>(DevelopmentIdentityHandler.SchemeName, null);
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Roles.Responder, policy => policy.RequireRole(Roles.Responder))
    .AddPolicy(Roles.Approver, policy => policy.RequireRole(Roles.Approver));

await using WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

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
    Roles = user.FindAll(ClaimTypes.Role).Select(role => role.Value)
});

api.MapGet("/incidents", async (int? limit, IRuntimeIncidents incidents, CancellationToken cancellationToken) =>
    await incidents.ListAsync(Math.Clamp(limit ?? 50, 1, 200), cancellationToken));

api.MapGet("/incidents/{incidentId:guid}", async (Guid incidentId, IRuntimeIncidents incidents, CancellationToken cancellationToken) =>
    await incidents.GetAsync(IncidentId.From(incidentId), cancellationToken) is { } detail ? Results.Ok(detail) : Results.NotFound());

api.MapGet("/incidents/{incidentId:guid}/run-record", async (Guid incidentId, IRuntimeIncidents incidents, CancellationToken cancellationToken) =>
    await incidents.RunRecordAsync(IncidentId.From(incidentId), cancellationToken) is { } record ? Results.Ok(record) : Results.NotFound());

api.MapPost("/incidents/{incidentId:guid}/plans/{planId:guid}/approve", async (Guid incidentId, Guid planId, ApprovePlanRequest request, ClaimsPrincipal user, ApprovalService approvals, CancellationToken cancellationToken) =>
    {
        Result<ApprovalReceipt> result = await approvals.ApproveAsync(
            new ApprovePlan(IncidentId.From(incidentId), PlanId.From(planId), request.PlanVersion, request.PlanHash, user.FindFirstValue(ClaimTypes.NameIdentifier)!),
            cancellationToken);
        return result.IsSuccess ? Results.Accepted($"/api/v1/incidents/{incidentId}", result.Value) : result.Error.ToProblem();
    })
    .RequireAuthorization(Roles.Approver);

api.MapPost("/incidents/{incidentId:guid}/plans/{planId:guid}/reject", async (Guid incidentId, Guid planId, RejectPlanRequest request, ClaimsPrincipal user, ApprovalService approvals, CancellationToken cancellationToken) =>
    {
        Result<ApprovalReceipt> result = await approvals.RejectAsync(
            new RejectPlan(IncidentId.From(incidentId), PlanId.From(planId), request.PlanVersion, request.Reason, user.FindFirstValue(ClaimTypes.NameIdentifier)!),
            cancellationToken);
        return result.IsSuccess ? Results.Accepted($"/api/v1/incidents/{incidentId}", result.Value) : result.Error.ToProblem();
    })
    .RequireAuthorization(Roles.Approver);

api.MapPost("/incidents/{incidentId:guid}/plans/{planId:guid}/revoke", async (Guid incidentId, Guid planId, RejectPlanRequest request, ClaimsPrincipal user, ApprovalService approvals, CancellationToken cancellationToken) =>
    {
        Result<ApprovalReceipt> result = await approvals.RevokeAsync(
            new RevokeApproval(IncidentId.From(incidentId), PlanId.From(planId), request.PlanVersion, request.Reason, user.FindFirstValue(ClaimTypes.NameIdentifier)!),
            cancellationToken);
        return result.IsSuccess ? Results.Accepted($"/api/v1/incidents/{incidentId}", result.Value) : result.Error.ToProblem();
    })
    .RequireAuthorization(Roles.Approver);

await app.RunAsync();

public partial class Program;
