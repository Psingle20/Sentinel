using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Reference.Common.Telemetry;

public static class ReferenceTelemetryExtensions
{
    // One options name per signal and target. OtlpExporterOptions are named options cached per name,
    // so exporters that share a name share one instance, and the endpoint configured last wins: with a
    // single name for all three signals, logs and traces were sent to the metrics endpoint.
    private const string StackLogs = "reference-stack-logs";
    private const string StackMetrics = "reference-stack-metrics";
    private const string StackTraces = "reference-stack-traces";
    private const string DashboardLogs = "aspire-dashboard-logs";
    private const string DashboardMetrics = "aspire-dashboard-metrics";
    private const string DashboardTraces = "aspire-dashboard-traces";

    public static IHostApplicationBuilder AddReferenceTelemetry(this IHostApplicationBuilder builder, string serviceName, params string[] sources)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ReferenceTelemetryOptions options = builder.Configuration.GetSection("Reference:Telemetry").Get<ReferenceTelemetryOptions>() ?? new();
        var dashboard = DashboardTarget.From(builder.Configuration);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment.name", "reference")]))
            .WithLogging(logging =>
            {
                if (options.LogsEndpoint is { } logs)
                {
                    logging.AddOtlpExporter(StackLogs, exporter => UseHttp(exporter, logs));
                }

                if (dashboard is not null)
                {
                    logging.AddOtlpExporter(DashboardLogs, dashboard.Apply);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter(sources);

                if (options.MetricsEndpoint is { } endpoint)
                {
                    metrics.AddOtlpExporter(StackMetrics, (exporter, reader) =>
                    {
                        UseHttp(exporter, endpoint);
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = options.MetricExportIntervalMilliseconds;
                        reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
                    });
                }

                if (dashboard is not null)
                {
                    metrics.AddOtlpExporter(DashboardMetrics, (exporter, reader) =>
                    {
                        dashboard.Apply(exporter);
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = options.MetricExportIntervalMilliseconds;
                    });
                }
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(aspNet => aspNet.Filter = IsTraced)
                    .AddHttpClientInstrumentation()
                    .AddSource(sources);

                if (options.TracesEndpoint is { } traces)
                {
                    tracing.AddOtlpExporter(StackTraces, exporter => UseHttp(exporter, traces));
                }

                if (dashboard is not null)
                {
                    tracing.AddOtlpExporter(DashboardTraces, dashboard.Apply);
                }
            });

        return builder;
    }

    // Control, health and telemetry endpoints are not part of the system's behaviour.
    private static bool IsTraced(HttpContext context) =>
        !context.Request.Path.StartsWithSegments("/_reference", StringComparison.OrdinalIgnoreCase)
        && !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    private static void UseHttp(OtlpExporterOptions exporter, Uri endpoint)
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
    }

    private sealed record DashboardTarget(Uri Endpoint, OtlpExportProtocol Protocol, string? Headers)
    {
        public static DashboardTarget? From(IConfiguration configuration)
        {
            string? endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return null;
            }

            OtlpExportProtocol protocol = string.Equals(configuration["OTEL_EXPORTER_OTLP_PROTOCOL"], "http/protobuf", StringComparison.OrdinalIgnoreCase)
                ? OtlpExportProtocol.HttpProtobuf
                : OtlpExportProtocol.Grpc;
            return new DashboardTarget(new Uri(endpoint), protocol, configuration["OTEL_EXPORTER_OTLP_HEADERS"]);
        }

        public void Apply(OtlpExporterOptions exporter)
        {
            exporter.Endpoint = Endpoint;
            exporter.Protocol = Protocol;
            if (Headers is not null)
            {
                exporter.Headers = Headers;
            }
        }
    }
}
