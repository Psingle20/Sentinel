using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Providers;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Reads spans and the service dependency graph from Jaeger's query API. Each span becomes a
/// <see cref="TraceObservation"/>; a span that calls another service records it as its dependency.
/// </summary>
internal sealed class JaegerProvider(HttpClient http, TimeProvider time, ILogger<JaegerProvider> logger) : ITracesProvider, ITopologyProvider
{
    public const string ProviderName = "jaeger";

    public string Name => ProviderName;

    public async Task<ProviderResult<TraceObservation>> QueryRangeAsync(TraceRangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var spans = new Dictionary<string, TraceObservation>(StringComparer.Ordinal);
            foreach (string service in request.Source.Services)
            {
                // Slow traces are the evidence; a small sample of the rest shows what normal looks like.
                await CollectAsync(request, service, $"&minDuration={(int)request.Source.SlowerThan.TotalMilliseconds}ms", request.Source.SlowTraceLimit, spans, cancellationToken);
                await CollectAsync(request, service, string.Empty, request.Source.SampleTraceLimit, spans, cancellationToken);
            }

            return new ProviderResult<TraceObservation>([.. spans.Values.OrderBy(span => span.EventTime)], request.Range.To, false, ProviderStatus.Healthy);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Jaeger trace query for {Source} failed", request.Source.Name);
            return ProviderResult<TraceObservation>.Unavailable(request.Range.From, exception.Message);
        }
    }

    public async Task<ProviderResult<TopologyObservation>> GetDependenciesAsync(TopologyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        long end = request.Range.To.ToUnixTimeMilliseconds();
        long lookback = (long)(request.Range.To - request.Range.From).TotalMilliseconds;
        try
        {
            using JsonDocument document = (await http.GetFromJsonAsync<JsonDocument>(
                string.Create(CultureInfo.InvariantCulture, $"/api/dependencies?endTs={end}&lookback={lookback}"), cancellationToken))!;
            DateTimeOffset now = time.GetUtcNow();
            var edges = new List<TopologyObservation>();
            foreach (JsonElement edge in document.RootElement.GetProperty("data").EnumerateArray())
            {
                if (!TryService(edge.GetProperty("parent").GetString(), out ServiceId parent) || !TryService(edge.GetProperty("child").GetString(), out ServiceId child))
                {
                    continue;
                }

                long calls = edge.TryGetProperty("callCount", out JsonElement count) ? count.GetInt64() : 0;
                edges.Add(new TopologyObservation
                {
                    ObservationId = ObservationId.From(string.Create(CultureInfo.InvariantCulture, $"{ProviderName}:dependency:{parent}:{child}:{end / 60_000}")),
                    TenantId = request.TenantId,
                    EnvironmentId = request.EnvironmentId,
                    ServiceId = parent,
                    SourceProvider = ProviderName,
                    SourceResource = "dependencies",
                    EventTime = request.Range.To,
                    IngestTime = now,
                    TargetServiceId = child,
                    DependencyType = "calls",
                    Protocol = "http",
                    Confidence = Math.Min(1, calls / 10.0),
                    FirstSeen = request.Range.From,
                    LastSeen = request.Range.To,
                    Attributes = new Dictionary<string, string> { ["callCount"] = calls.ToString(CultureInfo.InvariantCulture) }
                });
            }

            return new ProviderResult<TopologyObservation>(edges, request.Range.To, false, ProviderStatus.Healthy);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Jaeger dependency query failed");
            return ProviderResult<TopologyObservation>.Unavailable(request.Range.From, exception.Message);
        }
    }

    private async Task CollectAsync(TraceRangeRequest request, string service, string filter, int limit, Dictionary<string, TraceObservation> spans, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return;
        }

        string uri = string.Create(
            CultureInfo.InvariantCulture,
            $"/api/traces?service={Uri.EscapeDataString(service)}&start={Microseconds(request.Range.From)}&end={Microseconds(request.Range.To)}&limit={limit}{filter}");
        using JsonDocument document = (await http.GetFromJsonAsync<JsonDocument>(uri, cancellationToken))!;
        DateTimeOffset now = time.GetUtcNow();

        foreach (JsonElement trace in document.RootElement.GetProperty("data").EnumerateArray())
        {
            var processes = trace.GetProperty("processes").EnumerateObject()
                .ToDictionary(process => process.Name, process => process.Value.GetProperty("serviceName").GetString() ?? string.Empty, StringComparer.Ordinal);
            JsonElement[] traceSpans = [.. trace.GetProperty("spans").EnumerateArray()];
            Dictionary<string, string> serviceOfSpan = traceSpans.ToDictionary(
                span => span.GetProperty("spanID").GetString()!,
                span => processes.GetValueOrDefault(span.GetProperty("processID").GetString()!) ?? string.Empty,
                StringComparer.Ordinal);

            // A span whose child runs in another service is a call to that service.
            var dependencyOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonElement span in traceSpans)
            {
                if (Parent(span) is { } parent && serviceOfSpan.TryGetValue(parent, out string? parentService) && parentService != serviceOfSpan[span.GetProperty("spanID").GetString()!])
                {
                    dependencyOf[parent] = serviceOfSpan[span.GetProperty("spanID").GetString()!];
                }
            }

            foreach (JsonElement span in traceSpans)
            {
                string spanId = span.GetProperty("spanID").GetString()!;
                if (!TryService(serviceOfSpan[spanId], out ServiceId serviceId) || spans.ContainsKey(spanId))
                {
                    continue;
                }

                string traceId = span.GetProperty("traceID").GetString()!;
                DateTimeOffset start = DateTimeOffset.UnixEpoch.AddTicks(span.GetProperty("startTime").GetInt64() * 10);
                spans[spanId] = new TraceObservation
                {
                    ObservationId = ObservationId.From($"{ProviderName}:{traceId}:{spanId}"),
                    TenantId = request.TenantId,
                    EnvironmentId = request.EnvironmentId,
                    ServiceId = serviceId,
                    SourceProvider = ProviderName,
                    SourceResource = $"traces/{traceId}",
                    EventTime = start,
                    IngestTime = now,
                    TraceContext = new TraceContext(traceId, spanId),
                    TraceId = traceId,
                    SpanId = spanId,
                    ParentSpanId = Parent(span),
                    OperationName = span.GetProperty("operationName").GetString() ?? "unknown",
                    Duration = TimeSpan.FromTicks(span.GetProperty("duration").GetInt64() * 10),
                    Status = IsError(span) ? SpanStatus.Error : SpanStatus.Ok,
                    DependencyServiceId = dependencyOf.TryGetValue(spanId, out string? dependency) && TryService(dependency, out ServiceId dependencyId) ? dependencyId : null
                };
            }
        }
    }

    private static string? Parent(JsonElement span) =>
        span.TryGetProperty("references", out JsonElement references)
            ? references.EnumerateArray()
                .Where(reference => reference.GetProperty("refType").GetString() == "CHILD_OF")
                .Select(reference => reference.GetProperty("spanID").GetString())
                .FirstOrDefault()
            : null;

    private static bool IsError(JsonElement span) =>
        span.TryGetProperty("tags", out JsonElement tags) && tags.EnumerateArray().Any(tag =>
            tag.GetProperty("key").GetString() switch
            {
                "error" => tag.GetProperty("value").ValueKind == JsonValueKind.True || tag.GetProperty("value").ToString() == "true",
                "otel.status_code" => tag.GetProperty("value").GetString() == "ERROR",
                _ => false
            });

    private static long Microseconds(DateTimeOffset value) => (value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;

    private static bool TryService(string? value, out ServiceId serviceId)
    {
        serviceId = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            serviceId = ServiceId.From(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
