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

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureOpenTelemetry();

        // Dependency checks register with ReadyTag; "self" only proves the process is responsive.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        builder.Services.AddSentinelJson();
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

    private static bool IsHealthRequest(PathString path) =>
        path.StartsWithSegments(HealthPathPrefix, StringComparison.OrdinalIgnoreCase);
}
