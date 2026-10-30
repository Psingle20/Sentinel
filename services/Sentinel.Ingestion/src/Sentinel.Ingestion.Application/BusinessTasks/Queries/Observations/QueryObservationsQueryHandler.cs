using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Persistence;
using Sentinel.Ingestion.Application.Options;
using Sentinel.Ingestion.Domain.Observations;
using Sentinel.Ingestion.Domain.Sources;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries.Observations;

/// <summary>
/// The read-only observation query contract (ADR-007), answered from the observation store inside the
/// query bulkhead, with metadata that says what the answer can be trusted for: the watermark of the
/// sources it depends on, whether they all answered, and whether limits cut it short.
/// </summary>
public sealed class QueryObservationsQueryHandler(
    IObservationStore store,
    ISourceStateStore states,
    QueryBulkhead bulkhead,
    TimeProvider time,
    IOptions<IngestionOptions> options) : IQueryHandler<QueryObservationsQuery, ObservationQueryResult>
{
    /// <summary>Filter value that groups log lines by signature: bounded provider-side aggregation (ADR-007).</summary>
    public const string SignatureAggregation = "signature";

    public const int MaxLimit = 5000;

    public Task<Result<ObservationQueryResult>> Handle(QueryObservationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return bulkhead.RunAsync(token => AnswerAsync(query.Query, token), cancellationToken);
    }

    /// <summary>The sources whose data the query's answer depends on.</summary>
    public static IReadOnlyList<SourceState> Relevant(IReadOnlyList<SourceState> sources, ObservationQuery query)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(query);

        return query.Kind switch
        {
            ObservationKind.Metric when query.Filters.TryGetValue("metricName", out string? metric) =>
                [.. sources.Where(source => source.SourceId.EndsWith($":{metric}", StringComparison.Ordinal))],
            ObservationKind.Metric => [.. sources.Where(IsMetricSource)],
            ObservationKind.Deployment => [.. sources.Where(source => source.SourceId.EndsWith(":deployments", StringComparison.Ordinal))],
            ObservationKind.Log => [.. sources.Where(source => source.SourceId.Contains(":logs:", StringComparison.Ordinal))],
            ObservationKind.Trace => [.. sources.Where(source => source.SourceId.Contains(":traces:", StringComparison.Ordinal))],
            ObservationKind.Topology => [.. sources.Where(source => source.SourceId.EndsWith(":topology", StringComparison.Ordinal))],
            ObservationKind.Alert => [.. sources.Where(source => source.SourceId.EndsWith(":alerts", StringComparison.Ordinal))],
            _ => []
        };
    }

    private async Task<Result<ObservationQueryResult>> AnswerAsync(ObservationQuery query, CancellationToken cancellationToken)
    {
        IngestionOptions settings = options.Value;
        if (query.TenantId.Value != settings.Tenant || query.EnvironmentId.Value != settings.Environment)
        {
            return Result.Failure<ObservationQueryResult>(QueryErrors.UnknownScope);
        }

        if (query.Range.To <= query.Range.From)
        {
            return Result.Failure<ObservationQueryResult>(QueryErrors.InvalidRange);
        }

        bool bySignature = query.Kind == ObservationKind.Log && query.Filters.TryGetValue("aggregate", out string? aggregate) && aggregate == SignatureAggregation;
        ObservationPage page = await store.QueryAsync(query with { Limit = bySignature ? MaxLimit : Math.Clamp(query.Limit, 1, MaxLimit) }, cancellationToken);
        if (bySignature)
        {
            page = Aggregate(page, Math.Clamp(query.Limit, 1, MaxLimit));
        }
        IReadOnlyList<SourceState> sources = Relevant(await states.GetAllAsync(cancellationToken), query);

        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset observedThrough = sources.Count == 0 ? query.Range.From : sources.Min(source => source.Watermark);
        bool allAnswered = sources.Count > 0 && sources.All(source => source.Health == SourceHealth.Healthy);

        var metadata = new QueryMetadata
        {
            ObservedThrough = observedThrough,
            Freshness = now > observedThrough ? now - observedThrough : TimeSpan.Zero,
            Completeness = allAnswered ? QueryCompleteness.Complete : QueryCompleteness.Partial,
            Truncated = page.Truncated,
            ProviderHealth = [.. sources
                .GroupBy(source => source.Provider, StringComparer.Ordinal)
                .Select(group => new ProviderHealth(group.Key, Worst(group)))],
            RedactionPolicyVersion = RedactionPolicy.Version,
            ContinuationToken = page.ContinuationToken
        };

        return Result.Success(new ObservationQueryResult(page.Observations, metadata));
    }

    /// <summary>One representative line per signature, with how often it occurred and when.</summary>
    private static ObservationPage Aggregate(ObservationPage page, int limit)
    {
        LogObservation[] groups = [.. page.Observations.OfType<LogObservation>()
            .GroupBy(log => (log.ServiceId, Signature: log.Attributes.GetValueOrDefault("signature") ?? log.Message))
            .Select(group => group.OrderBy(log => log.EventTime).First() with
            {
                Attributes = new Dictionary<string, string>(group.First().Attributes, StringComparer.Ordinal)
                {
                    ["count"] = group.Count().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["firstSeen"] = group.Min(log => log.EventTime).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    ["lastSeen"] = group.Max(log => log.EventTime).ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                }
            })
            .OrderByDescending(log => int.Parse(log.Attributes["count"], System.Globalization.CultureInfo.InvariantCulture))];
        return new ObservationPage([.. groups.Take(limit)], page.Truncated || groups.Length > limit, null);
    }

    // Source IDs follow IngestionCycle's scheme; metric sources are the ones that are not another kind.
    private static bool IsMetricSource(SourceState source) =>
        !source.SourceId.EndsWith(":deployments", StringComparison.Ordinal)
        && !source.SourceId.EndsWith(":topology", StringComparison.Ordinal)
        && !source.SourceId.EndsWith(":alerts", StringComparison.Ordinal)
        && !source.SourceId.Contains(":logs:", StringComparison.Ordinal)
        && !source.SourceId.Contains(":traces:", StringComparison.Ordinal);

    private static ProviderStatus Worst(IEnumerable<SourceState> sources) =>
        sources.Max(source => source.Health) switch
        {
            SourceHealth.Healthy => ProviderStatus.Healthy,
            SourceHealth.Degraded => ProviderStatus.Degraded,
            _ => ProviderStatus.Unavailable
        };
}
