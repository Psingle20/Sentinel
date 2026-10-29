using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Sentinel.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public const string LivenessPath = "/health/live";
    public const string ReadinessPath = "/health/ready";
    public const string LiveTag = "live";
    public const string ReadyTag = "ready";

    private const string HealthPathPrefix = "/health";
    private const string SentinelInstrumentation = "Sentinel.*";
    private const string BuildingBlocksInstrumentation = "CleanArchitecture.BuildingBlocks.*";
    private const string NpgsqlInstrumentation = "Npgsql";
    private const string OtlpEndpointSetting = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>The response header that names the request's trace, so a caller can quote it when reporting a problem.</summary>
    public const string CorrelationHeader = "X-Correlation-Id";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureOpenTelemetry();

        // Dependency checks register with ReadyTag; "self" only proves the process is responsive.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        builder.Services.AddSentinelJson();

        // Every problem response carries its stable code and the W3C trace it belongs to as traceId (Phase 08 API principles).
        builder.Services.AddProblemDetails();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Unhandled exceptions and bare status codes become problem details; no stack trace leaves the service.
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.Use((context, next) =>
        {
            context.Response.Headers[CorrelationHeader] = CorrelationId(context);
            return next(context);
        });

        app.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(LiveTag)
        });
        app.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag)
        });

        return app;
    }

    private static void ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(SentinelInstrumentation))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(SentinelInstrumentation)
                .AddSource(BuildingBlocksInstrumentation)
                .AddSource(NpgsqlInstrumentation)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsHealthRequest(context.Request.Path))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointSetting]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
    }

    // The same value problem details report as traceId.
    private static string CorrelationId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;

    private static bool IsHealthRequest(PathString path) =>
        path.StartsWithSegments(HealthPathPrefix, StringComparison.OrdinalIgnoreCase);
}
