using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Host;
using Sentinel.Executor.Infrastructure;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddBuildingBlocks(Sentinel.Executor.Application.AssemblyReference.Assembly);

// Verification can take a minute or more; another consumer must not reclaim the request meanwhile.
builder.AddSentinelMessaging("executor", configureStreams: streams => streams.ClaimIdleTime = TimeSpan.FromMinutes(5))
    .Produces<RemediationExecuted>()
    .Consumes<ExecuteRemediation, ExecuteRemediationHandler>();
builder.Services.AddExecutorInfrastructure(builder.Configuration);

await using WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapOperatorEndpoints();

// Advisory plan-time policy evaluation for Runtime (ADR-006).
app.MapPost("/v1/policy/evaluations", async (PolicyEvaluationRequest request, PolicyService policy, CancellationToken cancellationToken) =>
    await policy.EvaluateAsync(request, cancellationToken));

// The current policy document, for operators and the control plane.
app.MapGet("/v1/policy", (IPolicyStore policies) => policies.Current);

// Operator controls independent of every model and workflow path (ADR-006). Phase 09 puts them behind operator authorization.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Sentinel:OperatorEndpoints:Enabled"))
{
    app.MapGet("/admin/controls", async (IControlStore controls, CancellationToken cancellationToken) => await controls.GetAsync(cancellationToken));
    app.MapPost("/admin/controls/emergency-stop", async (ControlChange change, IControlStore controls, IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
    {
        await controls.SetEmergencyStopAsync(change.Enabled, change.Actor, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    });
    app.MapPost("/admin/controls/actions/{actionKind}", async (string actionKind, ControlChange change, IControlStore controls, IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
    {
        await controls.SetActionDisabledAsync(actionKind, change.Enabled, change.Actor, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.NoContent();
    });
}

// The execution audit trail.
app.MapGet("/v1/executions", async (int? limit, IExecutionStore store, CancellationToken cancellationToken) =>
    (await store.RecentAsync(Math.Clamp(limit ?? 50, 1, 200), cancellationToken)).Select(request => request.Snapshot()));

await app.RunAsync();

public partial class Program;
