using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application;
using Sentinel.Ingestion.Application.Knowledge;
using Sentinel.Ingestion.Domain.Sources;
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

await app.RunAsync();

static IResult Respond<TValue>(Result<TValue> result) =>
    result.IsFailure && result.Error == QueryBulkhead.Full
        ? Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status503ServiceUnavailable, extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code })
        : result.ToHttpResult();

public partial class Program;
