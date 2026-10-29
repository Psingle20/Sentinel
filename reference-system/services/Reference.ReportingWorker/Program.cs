using Reference.Common;
using Reference.Common.Control;
using Reference.ReportingWorker.Batch;
using Reference.ReportingWorker.Revisions;
using Reference.ReportingWorker.Telemetry;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddReferenceDefaults(ReportingRevisions.ServiceName, ReportingRevisions.Baseline, ReportingRevisions.All, ReportingTelemetry.SourceName);
builder.Services.AddSingleton<ReportingBatch>();
builder.Services.AddSingleton<ReportingTelemetry>();

await using WebApplication app = builder.Build();

// Resolve the telemetry eagerly so the gauges exist before the first batch.
app.Services.GetRequiredService<ReportingTelemetry>();

app.MapGet("/api/batch", (ReportingBatch batch) => batch.Status());

if (app.MapReferenceControl() is { } control)
{
    control.MapPost("/batch/start", (StartBatchRequest? request, ReportingBatch batch) => batch.Start(request ?? new StartBatchRequest()));
    control.MapPost("/batch/stop", async (ReportingBatch batch) => await batch.StopAsync());
}

app.MapReferenceHealth();

await app.RunAsync();

public partial class Program;
