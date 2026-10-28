using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Providers;
using Sentinel.Ingestion.Domain.Observations;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Reads log lines from Loki's range query API and translates them into redacted
/// <see cref="LogObservation"/>s. Log text is untrusted data: it is redacted here, before it is
/// stored or reaches any investigator, and never interpreted.
/// </summary>
internal sealed class LokiLogsProvider(HttpClient http, TimeProvider time, ILogger<LokiLogsProvider> logger) : ILogsProvider
{
    public const string ProviderName = "loki";
    public const int MaxStackTraceLength = 4000;

    private static readonly HashSet<string> KeptFields = new(StringComparer.Ordinal) { "scope_name", "service_version", "deployment_environment_name" };

    public string Name => ProviderName;

    public async Task<ProviderResult<LogObservation>> QueryRangeAsync(LogRangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string uri = string.Create(
            CultureInfo.InvariantCulture,
            $"/loki/api/v1/query_range?query={Uri.EscapeDataString(request.Source.Query)}&start={Nanoseconds(request.Range.From)}&end={Nanoseconds(request.Range.To) - 1}&limit={request.Source.Limit}&direction=forward");
        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri(uri, UriKind.Relative), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ProviderResult<LogObservation>.Unavailable(request.Range.From, $"Loki returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
            }

            using JsonDocument document = (await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken))!;
            List<LogObservation> observations = Translate(request, document.RootElement.GetProperty("data").GetProperty("result"));
            bool truncated = observations.Count >= request.Source.Limit;
            return new ProviderResult<LogObservation>(observations, request.Range.To, truncated, ProviderStatus.Healthy);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Loki query for {Source} failed", request.Source.Name);
            return ProviderResult<LogObservation>.Unavailable(request.Range.From, exception.Message);
        }
    }

    private List<LogObservation> Translate(LogRangeRequest request, JsonElement streams)
    {
        DateTimeOffset ingestedAt = time.GetUtcNow();
        var observations = new List<LogObservation>();
        foreach (JsonElement stream in streams.EnumerateArray())
        {
            var labels = stream.GetProperty("stream").EnumerateObject()
                .ToDictionary(label => label.Name, label => label.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
            if (!labels.TryGetValue(request.Source.ServiceLabel, out string? service) || !TryService(service, out ServiceId serviceId))
            {
                continue;
            }

            foreach (JsonElement entry in stream.GetProperty("values").EnumerateArray())
            {
                string nanoseconds = entry[0].GetString()!;
                string line = entry[1].GetString() ?? string.Empty;
                observations.Add(Observation(request, serviceId, labels, nanoseconds, line, ingestedAt));
            }
        }

        return [.. observations.OrderBy(observation => observation.EventTime)];
    }

    private static LogObservation Observation(LogRangeRequest request, ServiceId serviceId, Dictionary<string, string> labels, string nanoseconds, string line, DateTimeOffset ingestedAt)
    {
        (string message, bool messageRedacted) = RedactionPolicy.Apply(line);
        (string exceptionMessage, bool exceptionRedacted) = RedactionPolicy.Apply(labels.GetValueOrDefault("exception_message"));
        (string stackTrace, bool stackRedacted) = RedactionPolicy.Apply(labels.GetValueOrDefault("exception_stacktrace"));
        string? traceId = labels.GetValueOrDefault("trace_id");
        string? spanId = labels.GetValueOrDefault("span_id");

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["signature"] = LogSignature.Of(message) };
        if (messageRedacted || exceptionRedacted || stackRedacted)
        {
            attributes["redacted"] = "true";
        }

        return new LogObservation
        {
            ObservationId = ObservationId.From($"{ProviderName}:{request.Source.Name}:{serviceId}:{nanoseconds}:{Fingerprint(line)}"),
            TenantId = request.TenantId,
            EnvironmentId = request.EnvironmentId,
            ServiceId = serviceId,
            SourceProvider = ProviderName,
            SourceResource = request.Source.Query,
            EventTime = FromNanoseconds(nanoseconds),
            IngestTime = ingestedAt,
            TraceContext = traceId is { Length: > 0 } && spanId is { Length: > 0 } ? new TraceContext(traceId, spanId) : null,
            Attributes = attributes,
            Severity = Severity(labels.GetValueOrDefault("severity_text") ?? labels.GetValueOrDefault("detected_level")),
            Message = message,
            StructuredFields = labels.Where(label => KeptFields.Contains(label.Key)).ToDictionary(label => label.Key, label => label.Value, StringComparer.Ordinal),
            ExceptionType = labels.GetValueOrDefault("exception_type"),
            ExceptionMessage = exceptionMessage.Length > 0 ? exceptionMessage : null,
            StackTrace = stackTrace.Length == 0 ? null : stackTrace[..Math.Min(stackTrace.Length, MaxStackTraceLength)]
        };
    }

    private static LogSeverity Severity(string? level) => level?.ToLowerInvariant() switch
    {
        "trace" => LogSeverity.Trace,
        "debug" => LogSeverity.Debug,
        "warning" or "warn" => LogSeverity.Warning,
        "error" => LogSeverity.Error,
        "critical" or "fatal" => LogSeverity.Critical,
        _ => LogSeverity.Information
    };

    private static string Fingerprint(string line) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line)))[..12];

    private static long Nanoseconds(DateTimeOffset value) => (value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) * 100;

    private static DateTimeOffset FromNanoseconds(string value) =>
        DateTimeOffset.UnixEpoch.AddTicks(long.Parse(value, CultureInfo.InvariantCulture) / 100);

    private static bool TryService(string value, out ServiceId serviceId)
    {
        serviceId = default;
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
