using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application;
using Sentinel.Ingestion.Application.Knowledge;
using Sentinel.Ingestion.Domain.Sources;
using Sentinel.Ingestion.Host;
using Sentinel.Ingestion.Infrastructure;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddBuildingBlocks(Sentinel.Ingestion.Application.AssemblyReference.Assembly);
builder.AddSentinelMessaging("ingestion", configureStreams: streams => streams.MaxStreamLength = 20_000)
    .Produces<ObservationRecorded>();
builder.Services.AddIngestionInfrastructure(builder.Configuration);

await using WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.MapOperatorEndpoints();

// The read-only observation query contract (ADR-007), used by AgentWorker tools and Runtime recovery
// checks. Queries pass the bulkhead, so a burst cannot starve the ingestion worker (Phase 02).
app.MapPost("/v1/observations/query", async (ObservationQuery query, ObservationQueryService queries, QueryBulkhead bulkhead, CancellationToken cancellationToken) =>
    Respond(await bulkhead.RunAsync(token => queries.QueryAsync(query, token), cancellationToken)));

// Runbook and documentation search for AgentWorker's knowledge tool.
app.MapPost("/v1/knowledge/search", async (KnowledgeQuery query, KnowledgeSearch knowledge, QueryBulkhead bulkhead, CancellationToken cancellationToken) =>
    Respond(await bulkhead.RunAsync(token => knowledge.SearchAsync(query, token), cancellationToken)));

// Provider health and watermarks, for operators.
app.MapGet("/v1/sources", async (ISourceStateStore states, CancellationToken cancellationToken) =>
    (await states.GetAllAsync(cancellationToken)).Select(state => new
    {
        state.SourceId,
        state.Provider,
        state.Watermark,
        Health = state.Health.ToString(),
        state.LastError,
        state.ConsecutiveFailures,
        Healthy = state.Health == SourceHealth.Healthy
    }));

// Phase 08 provider configuration: the control-plane document and every published version. Api authorizes and audits changes.
app.MapGet("/v1/configuration", async (SourceConfigurationService configuration, CancellationToken cancellationToken) =>
    await configuration.CurrentAsync(cancellationToken));
app.MapGet("/v1/configuration/history", async (SourceConfigurationService configuration, CancellationToken cancellationToken) =>
    await configuration.HistoryAsync(cancellationToken));

// Until service-to-service authentication (Phase 09), publishing exists only where operator endpoints are enabled.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Sentinel:OperatorEndpoints:Enabled"))
{
    app.MapPut("/admin/configuration", async (PublishSourceConfigurationRequest request, SourceConfigurationService configuration, ManagedDocument<SourceConfigurationDocument> current, CancellationToken cancellationToken) =>
    {
        Result<PublishedDocument<SourceConfigurationDocument>> published = await configuration.PublishAsync(request.Document, request.ExpectedVersion, request.Author, cancellationToken);
        if (published.IsFailure)
        {
            return published.ToHttpResult();
        }

        current.Set(published.Value);
        return Results.Ok(published.Value.Version);
    });
}

await app.RunAsync();

static IResult Respond<TValue>(Result<TValue> result) =>
    result.IsFailure && result.Error == QueryBulkhead.Full
        ? Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status503ServiceUnavailable, extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code })
        : result.ToHttpResult();

public partial class Program;
