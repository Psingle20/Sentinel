using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Primitives;
using Reference.Common.Control;
using Reference.Common.Telemetry;
using Reference.DeployController;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddReferenceTelemetry("deploy-controller");
DeployControllerOptions options = builder.Configuration.GetSection("DeployController").Get<DeployControllerOptions>() ?? new();
if (string.IsNullOrWhiteSpace(options.DeployToken) || string.IsNullOrWhiteSpace(options.ReadToken))
{
    throw new InvalidOperationException("DeployController:DeployToken and DeployController:ReadToken must be configured.");
}

builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddSingleton(new DeployControllerOptionsAccessor(options));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddFailureControl(builder.Configuration, builder.Environment);
builder.Services.AddHttpClient<RevisionClient>(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<DeploymentFaults>();
builder.Services.AddSingleton<DeploymentLedger>();
builder.Services.AddHealthChecks();

await using WebApplication app = builder.Build();

RouteGroupBuilder api = app.MapGroup("/api");

RouteGroupBuilder reads = api.MapGroup(string.Empty).AddEndpointFilter(RequireToken(options.ReadToken, options.DeployToken));
reads.MapGet("/services", async (DeploymentLedger ledger, CancellationToken cancellationToken) =>
{
    var views = new List<ServiceView>();
    foreach (string service in options.Services.Keys)
    {
        views.Add(await DescribeAsync(service, ledger, cancellationToken));
    }

    return views;
});
reads.MapGet("/services/{service}", async (string service, DeploymentLedger ledger, CancellationToken cancellationToken) =>
    options.Services.ContainsKey(service) ? Results.Ok(await DescribeAsync(service, ledger, cancellationToken)) : Results.NotFound());
reads.MapGet("/deployments", (DateTimeOffset? since, DateTimeOffset? until, string? service, DeploymentLedger ledger) =>
    ledger.Query(since, until, service));
reads.MapGet("/deployments/{deploymentId}", (string deploymentId, DeploymentLedger ledger) =>
    ledger.Find(deploymentId) is { } record ? Results.Ok(record) : Results.NotFound());

api.MapPost("/services/{service}/deployments", async (string service, DeployRequest request, HttpRequest http, DeploymentLedger ledger, CancellationToken cancellationToken) =>
    {
        string? idempotencyKey = http.Headers.TryGetValue("Idempotency-Key", out StringValues key) ? key.ToString() : null;
        DeployResult result = await ledger.StartAsync(service, request, idempotencyKey, cancellationToken);
        return result.Outcome switch
        {
            DeployOutcome.Accepted => Results.Accepted($"/api/deployments/{result.Deployment!.DeploymentId}", result.Deployment),
            DeployOutcome.Replayed => Results.Ok(result.Deployment),
            DeployOutcome.Conflict => Results.Conflict(result.Deployment),
            DeployOutcome.UnknownService => Results.NotFound(),
            _ => Results.BadRequest($"{request.Version} is not a revision of {service}.")
        };
    })
    .AddEndpointFilter(RequireToken(options.DeployToken));

if (app.Services.GetRequiredService<FailureControl>() is { Enabled: true } control)
{
    app.MapPost("/_reference/reset", async (DeploymentLedger ledger, CancellationToken cancellationToken) => await ledger.ResetAsync(cancellationToken))
        .AddEndpointFilter(async (context, next) =>
            control.IsAuthorized(context.HttpContext.Request) ? await next(context) : Results.Unauthorized());

    // Phase 07 failure scenarios: make the next rollbacks fail until reset.
    app.MapPost("/_reference/faults", (DeploymentFaultsRequest request, DeploymentFaults faults) =>
        {
            faults.FailRollbacks = request.FailRollbacks;
            return Results.NoContent();
        })
        .AddEndpointFilter(async (context, next) =>
            control.IsAuthorized(context.HttpContext.Request) ? await next(context) : Results.Unauthorized());
}

app.MapHealthChecks("/health");

await app.RunAsync();

static async Task<ServiceView> DescribeAsync(string service, DeploymentLedger ledger, CancellationToken cancellationToken)
{
    ManagedService managed = ledger.Options.Services[service];
    try
    {
        string? serving = await ledger.ServingRevisionAsync(service, cancellationToken);
        return new ServiceView(service, serving, managed.Baseline, managed.Revisions, ledger.Active(service), null);
    }
    catch (HttpRequestException exception)
    {
        return new ServiceView(service, null, managed.Baseline, managed.Revisions, ledger.Active(service), exception.Message);
    }
}

// Bearer tokens compared in constant time. Reads accept either token; deployments need the deploy token.
static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RequireToken(params string?[] accepted)
{
    byte[][] tokens = [.. accepted.Where(token => !string.IsNullOrEmpty(token)).Select(token => Encoding.UTF8.GetBytes(token!))];
    return async (context, next) =>
    {
        string header = context.HttpContext.Request.Headers.Authorization.ToString();
        byte[] presented = Encoding.UTF8.GetBytes(header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..] : string.Empty);
        return tokens.Any(token => CryptographicOperations.FixedTimeEquals(token, presented))
            ? await next(context)
            : Results.Unauthorized();
    };
}

public partial class Program;
