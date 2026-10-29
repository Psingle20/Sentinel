using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using OpenTelemetry.Trace;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.ControlPlane;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Api;
using Sentinel.Runtime.Infrastructure;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource([.. RuntimeInfrastructure.TraceSources]));
builder.Services.AddBuildingBlocks(Sentinel.Runtime.Application.AssemblyReference.Assembly);

// The silo starts before the message consumers, so the first message finds its grains ready.
builder.AddRuntimeActors();
builder.AddSentinelMessaging("runtime")
    .Produces<IncidentOpened>()
    .Produces<IncidentStatusChanged>()
    .Produces<IncidentBlackboardUpdated>()
    .Produces<DecisionRecorded>()
    .Produces<RemediationPlanProposed>()
    .Produces<RecoveryEvaluated>()
    .Produces<InvestigateIncident>()
    .Produces<ExecuteRemediation>()
    .Consumes<ObservationRecorded, ObservationRecordedHandler>()
    .Consumes<InvestigationCompleted, InvestigationCompletedHandler>()
    .Consumes<RecordApproval, RecordApprovalHandler>()
    .Consumes<RecordRejection, RecordRejectionHandler>()
    .Consumes<RecordRevocation, RecordRevocationHandler>()
    .Consumes<RemediationExecuted, RemediationExecutedHandler>();
builder.Services.AddRuntimeInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddRuntimeHealthChecks(ServiceDefaultsExtensions.ReadyTag);

await using WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapOperatorEndpoints();

// Incident queries for Api, served from the read model.
app.MapGet("/v1/incidents", async (int? limit, IncidentQueryService queries, CancellationToken cancellationToken) =>
    await queries.ListAsync(limit ?? 50, cancellationToken));
app.MapGet("/v1/incidents/{incidentId:guid}", async (Guid incidentId, IncidentQueryService queries, CancellationToken cancellationToken) =>
    (await queries.GetAsync(IncidentId.From(incidentId), cancellationToken)).ToHttpResult());

// Phase 07: the schema-2 run record, the postmortem generated from the stream, incident memory and similar incidents.
app.MapGet("/v1/incidents/{incidentId:guid}/run-record", async (Guid incidentId, string? dataset, PostmortemService postmortems, CancellationToken cancellationToken) =>
    await postmortems.RunRecordAsync(IncidentId.From(incidentId), dataset, cancellationToken) is { } record ? Results.Ok(record) : Results.NotFound());
app.MapGet("/v1/incidents/{incidentId:guid}/postmortem", async (Guid incidentId, PostmortemService postmortems, CancellationToken cancellationToken) =>
    await postmortems.BuildAsync(IncidentId.From(incidentId), cancellationToken) is { } postmortem ? Results.Ok(postmortem) : Results.NotFound());
app.MapGet("/v1/incidents/{incidentId:guid}/similar", async (Guid incidentId, IncidentMemoryService memory, CancellationToken cancellationToken) =>
    await memory.SimilarToAsync(IncidentId.From(incidentId), cancellationToken) is { } similar ? Results.Ok(similar) : Results.NotFound());
app.MapGet("/v1/memory/{incidentId:guid}", async (Guid incidentId, IncidentMemoryService memory, CancellationToken cancellationToken) =>
    await memory.GetAsync(IncidentId.From(incidentId), cancellationToken) is { } record ? Results.Ok(record) : Results.NotFound());

// Level-1 domain replay: read-only, so it is safe on any incident at any time.
app.MapPost("/v1/incidents/{incidentId:guid}/replays/domain", async (Guid incidentId, DomainReplayService replay, CancellationToken cancellationToken) =>
    await replay.ReplayAsync(IncidentId.From(incidentId), cancellationToken) is { } result ? Results.Ok(result) : Results.NotFound());

// The authoritative state from IncidentGrain beside the projection's version: projection lag (Phase 03).
app.MapGet("/v1/incidents/{incidentId:guid}/state", async (Guid incidentId, IncidentQueryService queries, CancellationToken cancellationToken) =>
    (await queries.StateAsync(IncidentId.From(incidentId), cancellationToken)).ToHttpResult());

// Executor's start confirmation, immediately before any side effect (ADR-006).
app.MapPost("/v1/remediation/start-confirmations", async (RemediationStartRequest request, RemediationControlService control, CancellationToken cancellationToken) =>
    await control.ConfirmStartAsync(request, cancellationToken));

// Phase 08: Runtime's control-plane document, with every published version. Api authorizes and audits changes.
app.MapGet("/v1/configuration", async (RuntimeConfigurationService configuration, CancellationToken cancellationToken) =>
    await configuration.CurrentAsync(cancellationToken));
app.MapGet("/v1/configuration/history", async (RuntimeConfigurationService configuration, CancellationToken cancellationToken) =>
    await configuration.HistoryAsync(cancellationToken));

// Operator tooling: rebuild every projection from its stream. It overwrites in place and deletes nothing.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Sentinel:OperatorEndpoints:Enabled"))
{
    app.MapPost("/admin/projections/rebuild", async (ProjectionRebuilder rebuilder, CancellationToken cancellationToken) =>
        Results.Ok(new { rebuilt = await rebuilder.RebuildAsync(cancellationToken) }));

    // A published version applies here at once and on other instances at their next refresh; workflows already running keep their timers.
    app.MapPut("/admin/configuration", async (PublishRuntimeConfigurationRequest request, RuntimeConfigurationService configuration, ManagedDocument<RuntimeConfigurationDocument> current, CancellationToken cancellationToken) =>
    {
        Result<PublishedDocument<RuntimeConfigurationDocument>> published = await configuration.PublishAsync(request.Document, request.ExpectedVersion, request.Author, cancellationToken);
        if (published.IsFailure)
        {
            return published.Error.ToProblem();
        }

        current.Set(published.Value);
        return Results.Ok(published.Value.Version);
    });

    // Retention controls (Phase 07). The purge is never scheduled and defaults to a dry run.
    app.MapPost("/admin/memory/{incidentId:guid}/legal-hold", async (Guid incidentId, LegalHoldRequest request, IncidentMemoryService memory, IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
    {
        IncidentMemoryView? updated = await memory.SetLegalHoldAsync(IncidentId.From(incidentId), request.Hold, request.Actor, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    });
    app.MapPost("/admin/memory/{incidentId:guid}/delete", async (Guid incidentId, MemoryDeletionRequest request, IncidentMemoryService memory, IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
    {
        Result deleted = await memory.DeleteAsync(IncidentId.From(incidentId), request.Actor, request.Reason, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return deleted.IsSuccess ? Results.NoContent() : deleted.Error.ToProblem();
    });
    app.MapPost("/admin/memory/purge", async (MemoryPurgeRequest request, IncidentMemoryService memory, IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
    {
        PurgeReport report = await memory.PurgeAsync(request.DryRun, request.Actor, request.Limit ?? 100, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(report);
    });
}

await app.RunAsync();

public partial class Program;
