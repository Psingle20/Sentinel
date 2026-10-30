using System.Collections.Concurrent;
using System.Globalization;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Application.Options;
using Sentinel.Ingestion.Domain.Observations;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Reads SQS queue attributes as metric observations (Phase E01, Stage 2). A series' expression names a queue and the
/// attributes its value adds up, as in <c>orders:ApproximateNumberOfMessages+ApproximateNumberOfMessagesNotVisible</c>,
/// and the series names its service, since a queue carries no service label. SQS reports what a queue holds now and keeps
/// no history, so each pull yields one sample, stamped at the step its reading falls in: a range Ingestion missed, while
/// SQS or Ingestion was down, stays without samples rather than being filled with a later reading.
/// </summary>
internal sealed class SqsQueueMetricsProvider(IAmazonSQS sqs, TimeProvider time, ILogger<SqsQueueMetricsProvider> logger) : IMetricsProvider
{
    public const string ProviderName = "sqs";

    private readonly ConcurrentDictionary<string, string> _urls = new(StringComparer.Ordinal);

    public string Name => ProviderName;

    public async Task<ProviderResult<MetricObservation>> QueryRangeAsync(MetricRangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        MetricSeriesDefinition series = request.Series;
        if (Parse(series.Expression) is not { } query)
        {
            return ProviderResult<MetricObservation>.Unavailable(request.Range.From, $"{series.Name}'s expression must name a queue and its attributes, as queue:Attribute+Attribute.");
        }

        if (!PrometheusMetricsProvider.TryServiceId(series.Service, out ServiceId service))
        {
            return ProviderResult<MetricObservation>.Unavailable(request.Range.From, $"{series.Name} must name its service; an SQS queue carries no service label.");
        }

        try
        {
            if (!_urls.TryGetValue(query.Queue, out string? url))
            {
                url = (await sqs.GetQueueUrlAsync(query.Queue, cancellationToken)).QueueUrl;
                _urls[query.Queue] = url;
            }

            GetQueueAttributesResponse response = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = url, AttributeNames = [.. query.Attributes] }, cancellationToken);
            double value = 0;
            foreach (string attribute in query.Attributes)
            {
                if (response.Attributes?.GetValueOrDefault(attribute) is not { } reported || !double.TryParse(reported, NumberStyles.Float, CultureInfo.InvariantCulture, out double part))
                {
                    return ProviderResult<MetricObservation>.Unavailable(request.Range.From, $"SQS did not report {attribute} for {query.Queue}.");
                }

                value += part;
            }

            DateTimeOffset now = time.GetUtcNow();
            DateTimeOffset sampledAt = new(now.UtcTicks - now.UtcTicks % request.Step.Ticks, TimeSpan.Zero);
            var observation = new MetricObservation
            {
                ObservationId = ObservationIdentity.Metric(ProviderName, series.Name, service, sampledAt),
                TenantId = request.TenantId,
                EnvironmentId = request.EnvironmentId,
                ServiceId = service,
                SourceProvider = ProviderName,
                SourceResource = series.Expression,
                EventTime = sampledAt,
                IngestTime = now,
                MetricName = series.Name,
                Value = value,
                Unit = series.Unit,
                Labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["queue"] = query.Queue },
                Aggregation = series.Aggregation,
                Window = series.Window
            };
            return new ProviderResult<MetricObservation>([observation], request.Range.To, false, ProviderStatus.Healthy);
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // A queue deleted and created again under its name has a new URL.
            _urls.TryRemove(query.Queue, out _);
            logger.LogWarning(exception, "Reading SQS attributes for {Series} failed", series.Name);
            return ProviderResult<MetricObservation>.Unavailable(request.Range.From, exception.Message);
        }
    }

    private static (string Queue, string[] Attributes)? Parse(string expression)
    {
        int colon = expression.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return null;
        }

        string[] attributes = expression[(colon + 1)..].Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return attributes.Length > 0 ? (expression[..colon].Trim(), attributes) : null;
    }
}
