using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Observations;
using Sentinel.Ingestion.Application.Abstractions.Providers;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Pulls metric series with the Prometheus HTTP API's range query and translates each sample
/// into a canonical <see cref="MetricObservation"/>. Prometheus formats stop here.
/// </summary>
internal sealed class PrometheusMetricsProvider(HttpClient http, TimeProvider time, ILogger<PrometheusMetricsProvider> logger) : IMetricsProvider
{
    public const string ProviderName = "prometheus";

    public string Name => ProviderName;

    public async Task<ProviderResult<MetricObservation>> QueryRangeAsync(MetricRangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The range is [From, To); Prometheus includes its end, so stop one step early.
        DateTimeOffset end = request.Range.To - request.Step;
        string uri = string.Create(
            CultureInfo.InvariantCulture,
            $"/api/v1/query_range?query={Uri.EscapeDataString(request.Series.Expression)}&start={request.Range.From.ToUnixTimeSeconds()}&end={end.ToUnixTimeSeconds()}&step={(int)request.Step.TotalSeconds}");

        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri(uri, UriKind.Relative), cancellationToken);
            using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
                ?? throw new JsonException("Prometheus returned an empty body.");
            JsonElement root = document.RootElement;

            if (!response.IsSuccessStatusCode || root.GetProperty("status").GetString() != "success")
            {
                string error = root.TryGetProperty("error", out JsonElement message) ? message.GetString() ?? "unknown error" : response.ReasonPhrase ?? "unknown error";
                return ProviderResult<MetricObservation>.Unavailable(request.Range.From, $"Prometheus rejected {request.Series.Name}: {error}");
            }

            return new ProviderResult<MetricObservation>(Translate(request, root.GetProperty("data").GetProperty("result")), request.Range.To, false, ProviderStatus.Healthy);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Prometheus query for {Series} failed", request.Series.Name);
            return ProviderResult<MetricObservation>.Unavailable(request.Range.From, exception.Message);
        }
    }

    private List<MetricObservation> Translate(MetricRangeRequest request, JsonElement series)
    {
        DateTimeOffset ingestedAt = time.GetUtcNow();
        var observations = new List<MetricObservation>();
        foreach (JsonElement entry in series.EnumerateArray())
        {
            var labels = entry.GetProperty("metric").EnumerateObject()
                .ToDictionary(label => label.Name, label => label.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
            string? service = request.Series.Service
                ?? (request.Series.ServiceLabel is { } label ? labels.GetValueOrDefault(label) : null);
            if (!TryServiceId(service, out ServiceId serviceId))
            {
                continue;
            }

            foreach (JsonElement sample in entry.GetProperty("values").EnumerateArray())
            {
                if (!double.TryParse(sample[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                {
                    continue;
                }

                var eventTime = DateTimeOffset.FromUnixTimeMilliseconds((long)(sample[0].GetDouble() * 1000));
                observations.Add(new MetricObservation
                {
                    ObservationId = ObservationIdentity.Metric(ProviderName, request.Series.Name, serviceId, eventTime),
                    TenantId = request.TenantId,
                    EnvironmentId = request.EnvironmentId,
                    ServiceId = serviceId,
                    SourceProvider = ProviderName,
                    SourceResource = request.Series.Expression,
                    EventTime = eventTime,
                    IngestTime = ingestedAt,
                    MetricName = request.Series.Name,
                    Value = value,
                    Unit = request.Series.Unit,
                    Labels = labels,
                    Aggregation = request.Series.Aggregation,
                    Window = request.Series.Window
                });
            }
        }

        return observations;
    }

    private static bool TryServiceId(string? value, out ServiceId serviceId)
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
