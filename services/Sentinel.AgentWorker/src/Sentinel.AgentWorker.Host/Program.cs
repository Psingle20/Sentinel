using CleanArchitecture.BuildingBlocks;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Infrastructure;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddBuildingBlocks(Sentinel.AgentWorker.Application.AssemblyReference.Assembly);

// An investigation may run for minutes; another consumer must not reclaim it while it does.
builder.AddSentinelMessaging("agentworker", configureStreams: streams => streams.ClaimIdleTime = TimeSpan.FromMinutes(5))
    .Produces<InvestigationCompleted>()
    .Consumes<InvestigateIncident, InvestigateIncidentHandler>();
builder.Services.AddAgentWorkerInfrastructure(builder.Configuration);

await using WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapOperatorEndpoints();

// Agent executions for an incident: investigator, model, tool calls and cost (Phase 08 agent activity view).
app.MapGet("/v1/executions", async (Guid incidentId, IExecutionStore executions, CancellationToken cancellationToken) =>
    await executions.ForIncidentAsync(IncidentId.From(incidentId), cancellationToken));
app.MapGet("/v1/executions/{executionId:guid}", async (Guid executionId, IExecutionStore executions, CancellationToken cancellationToken) =>
    await executions.GetAsync(AgentExecutionId.From(executionId), cancellationToken) is { } execution ? Results.Ok(execution) : Results.NotFound());

// Investigation replay (Phase 07, levels 2 and 3): served only from the execution's captured inputs, with no side effects.
app.MapPost("/v1/executions/{executionId:guid}/replays", async (Guid executionId, InvestigationReplayRequest request, InvestigationReplayService replays, CancellationToken cancellationToken) =>
{
    try
    {
        return await replays.ReplayAsync(AgentExecutionId.From(executionId), request, cancellationToken) is { } result ? Results.Ok(result) : Results.NotFound();
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});

await app.RunAsync();

public partial class Program;
