using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Models;
using Sentinel.AgentWorker.Api;
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

// Phase 08 model selection: the control-plane document and every published version. Api authorizes and audits changes.
app.MapGet("/v1/configuration", async (ModelConfigurationService configuration, CancellationToken cancellationToken) =>
    await configuration.CurrentAsync(cancellationToken));
app.MapGet("/v1/configuration/history", async (ModelConfigurationService configuration, CancellationToken cancellationToken) =>
    await configuration.HistoryAsync(cancellationToken));

// Until service-to-service authentication (Phase 09), publishing exists only where operator endpoints are enabled.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Sentinel:OperatorEndpoints:Enabled"))
{
    app.MapPut("/admin/configuration", async (PublishModelConfigurationRequest request, ModelConfigurationService configuration, ManagedDocument<ModelConfigurationDocument> current, CancellationToken cancellationToken) =>
    {
        Result<PublishedDocument<ModelConfigurationDocument>> published = await configuration.PublishAsync(request.Document, request.ExpectedVersion, request.Author, cancellationToken);
        if (published.IsFailure)
        {
            return Results.Problem(published.Error.Description, title: published.Error.Code, statusCode: published.Error.Type == ErrorType.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        }

        // Investigations already running keep the settings they started with.
        current.Set(published.Value);
        return Results.Ok(published.Value.Version);
    });
}

await app.RunAsync();

public partial class Program;
