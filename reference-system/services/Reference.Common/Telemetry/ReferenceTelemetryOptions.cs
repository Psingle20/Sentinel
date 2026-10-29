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

/// <summary>
/// Where the reference system sends telemetry: the observability stack Sentinel ingests from
/// (Prometheus, Loki and Jaeger over OTLP/HTTP). The Aspire dashboard also receives a copy when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, for people watching a scenario.
/// </summary>
public sealed class ReferenceTelemetryOptions
{
    public Uri? MetricsEndpoint { get; set; }

    public Uri? LogsEndpoint { get; set; }

    public Uri? TracesEndpoint { get; set; }

    /// <summary>Metric export interval. Short, so a scenario's symptoms show within seconds.</summary>
    public int MetricExportIntervalMilliseconds { get; set; } = 5000;
}
