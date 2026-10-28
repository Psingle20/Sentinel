using Reference.Common.Control;
using Reference.Common.Telemetry;
using Reference.Scenarios;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddReferenceTelemetry("scenario-runner", TrafficGenerator.SourceName);
string manifestDirectory = builder.Configuration["Reference:ManifestDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "manifests");
builder.Services.AddSingleton(ScenarioCatalog.Load(manifestDirectory));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddFailureControl(builder.Configuration, builder.Environment);
builder.Services.AddHttpClient(nameof(TrafficGenerator), client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient<ReferenceControlClient>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<TrafficGenerator>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TrafficGenerator>());
builder.Services.AddSingleton<ScenarioRunner>();
builder.Services.AddHealthChecks();

await using WebApplication app = builder.Build();

app.MapGet("/api/scenarios", (ScenarioCatalog catalog) =>
    catalog.Manifests.Values.OrderBy(manifest => manifest.Id, StringComparer.Ordinal)
        .Select(manifest => new { manifest.Id, manifest.Slug, manifest.Kind, manifest.Version, manifest.Description }));
app.MapGet("/api/scenarios/{scenarioId}", (string scenarioId, ScenarioCatalog catalog) =>
    catalog.Manifests.TryGetValue(scenarioId, out ScenarioManifest? manifest) ? Results.Ok(manifest) : Results.NotFound());
app.MapGet("/api/runs/current", (ScenarioRunner runner) => runner.Current is { } run ? Results.Ok(run) : Results.NoContent());
app.MapGet("/api/runs/{runId}", (string runId, ScenarioRunner runner) => runner.Find(runId) is { } run ? Results.Ok(run) : Results.NotFound());
app.MapGet("/api/traffic", (TrafficGenerator traffic) => traffic.Settings);

FailureControl control = app.Services.GetRequiredService<FailureControl>();
if (control.Enabled)
{
    RouteGroupBuilder guarded = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
        control.IsAuthorized(context.HttpContext.Request) ? await next(context) : Results.Unauthorized());

    guarded.MapPost("/runs", (StartRunRequest request, ScenarioRunner runner) =>
    {
        (StartRunOutcome outcome, ScenarioRunView? run) = runner.Start(request.ScenarioId);
        return outcome switch
        {
            StartRunOutcome.Started => Results.Accepted($"/api/runs/{run!.RunId}", run),
            StartRunOutcome.RunActive => Results.Conflict(run),
            _ => Results.NotFound()
        };
    });
    guarded.MapPost("/runs/{runId}/stop", (string runId, ScenarioRunner runner) => runner.Stop(runId) ? Results.NoContent() : Results.NotFound());
    guarded.MapPost("/reset", async (ReferenceControlClient reference, CancellationToken cancellationToken) =>
    {
        await reference.ResetAsync(cancellationToken);
        return Results.NoContent();
    });
    guarded.MapPost("/actions/stop-batch", async (ReferenceControlClient reference, CancellationToken cancellationToken) =>
    {
        await reference.StopBatchAsync(cancellationToken);
        return Results.NoContent();
    });
    guarded.MapPut("/traffic", (TrafficSettings settings, TrafficGenerator traffic) =>
    {
        traffic.Update(settings);
        return traffic.Settings;
    });
}

app.MapHealthChecks("/health");

await app.RunAsync();

public partial class Program;
