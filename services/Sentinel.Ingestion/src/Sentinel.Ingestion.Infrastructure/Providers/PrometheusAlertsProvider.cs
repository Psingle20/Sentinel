using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Domain.Observations;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Reads the alerts Prometheus is evaluating with its HTTP API's alerts endpoint and translates each pending or firing
/// alert into a canonical <see cref="AlertObservation"/>. An alert's identity is its rule, its labels and when it became
/// active, so reading it again adds nothing until it changes state; its annotation is redacted like any log line.
/// </summary>
internal sealed class PrometheusAlertsProvider(HttpClient http, TimeProvider time, ILogger<PrometheusAlertsProvider> logger) : IAlertProvider
{
    /// <summary>The label naming the service an alert concerns; an alert without one is not about a service and is skipped.</summary>
    public const string ServiceLabel = "service";

    public string Name => PrometheusMetricsProvider.ProviderName;

    public async Task<ProviderResult<AlertObservation>> GetActiveAlertsAsync(AlertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri("/api/v1/alerts", UriKind.Relative), cancellationToken);
            using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
                ?? throw new JsonException("Prometheus returned an empty body.");
            JsonElement root = document.RootElement;
            if (!response.IsSuccessStatusCode || root.GetProperty("status").GetString() != "success")
            {
                string error = root.TryGetProperty("error", out JsonElement message) ? message.GetString() ?? "unknown error" : response.ReasonPhrase ?? "unknown error";
                return ProviderResult<AlertObservation>.Unavailable(request.At, $"Prometheus rejected the alerts query: {error}");
            }

            return new ProviderResult<AlertObservation>(Translate(request, root.GetProperty("data").GetProperty("alerts")), request.At, false, ProviderStatus.Healthy);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Prometheus alerts query failed");
            return ProviderResult<AlertObservation>.Unavailable(request.At, exception.Message);
        }
    }

    private List<AlertObservation> Translate(AlertRequest request, JsonElement alerts)
    {
        DateTimeOffset ingestedAt = time.GetUtcNow();
        var observations = new List<AlertObservation>();
        foreach (JsonElement alert in alerts.EnumerateArray())
        {
            Dictionary<string, string> labels = Strings(alert, "labels");
            Dictionary<string, string> annotations = Strings(alert, "annotations");
            string state = alert.GetProperty("state").GetString() ?? string.Empty;
            if (state is not ("pending" or "firing")
                || !labels.TryGetValue("alertname", out string? name)
                || !PrometheusMetricsProvider.TryServiceId(labels.GetValueOrDefault(ServiceLabel), out ServiceId serviceId)
                || !DateTimeOffset.TryParse(alert.GetProperty("activeAt").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset activeAt))
            {
                continue;
            }

            (string summary, bool redacted) = RedactionPolicy.Apply(annotations.GetValueOrDefault("summary") ?? annotations.GetValueOrDefault("description"));
            var attributes = new Dictionary<string, string>(labels.Where(label => label.Key is not ("alertname" or "severity")), StringComparer.Ordinal);
            if (redacted)
            {
                attributes["redacted"] = "true";
            }

            observations.Add(new AlertObservation
            {
                ObservationId = ObservationId.From(string.Create(CultureInfo.InvariantCulture, $"{Name}:alert:{name}:{Fingerprint(labels)}:{activeAt.ToUnixTimeSeconds()}:{state}")),
                TenantId = request.TenantId,
                EnvironmentId = request.EnvironmentId,
                ServiceId = serviceId,
                SourceProvider = Name,
                SourceResource = $"alerts/{name}",
                EventTime = activeAt,
                IngestTime = ingestedAt,
                AlertName = name,
                State = state,
                Severity = labels.GetValueOrDefault("severity"),
                ActiveAt = activeAt,
                Value = Value(alert),
                Summary = summary.Length > 0 ? summary : null,
                Attributes = attributes
            });
        }

        return observations;
    }

    private static Dictionary<string, string> Strings(JsonElement alert, string property) =>
        alert.TryGetProperty(property, out JsonElement values) && values.ValueKind == JsonValueKind.Object
            ? values.EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    // Prometheus reports the expression's value as a string, and as "+Inf" or "NaN" when it is not a number.
    private static double? Value(JsonElement alert) =>
        alert.TryGetProperty("value", out JsonElement value)
        && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
        && double.IsFinite(parsed)
            ? parsed
            : null;

    // One alert rule can fire once per label set; the labels, in name order, tell those apart.
    private static string Fingerprint(Dictionary<string, string> labels)
    {
        string canonical = string.Join("\n", labels.OrderBy(label => label.Key, StringComparer.Ordinal).Select(label => $"{label.Key}={label.Value}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }
}
