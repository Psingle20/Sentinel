using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Tools;

/// <summary>Configured as <c>Investigation:Completeness</c>.</summary>
public sealed class CompletenessOptions
{
    public const string Section = "Investigation:Completeness";

    /// <summary>The longest one query waits for its sources to catch up with the end of its range.</summary>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>
/// Waits, briefly and boundedly, for data that is merely late (Phase 04, ADR-016). An investigation
/// starts seconds after detection, before logs and traces are indexed through the window's end, so
/// a first answer is often partial only because ingestion lags. When every source is healthy and
/// the answer is complete except that <c>ObservedThrough</c> trails the range, the query is asked
/// again until it catches up or the wait runs out; then the partial answer is returned as partial.
/// An unhealthy or truncated answer is never waited for.
/// </summary>
public sealed class PatientObservationQueries(IObservationQueries inner, Microsoft.Extensions.Options.IOptions<CompletenessOptions> options, TimeProvider time) : IObservationQueries
{
    public async Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        CompletenessOptions settings = options.Value;
        DateTimeOffset deadline = time.GetUtcNow() + settings.MaxWait;
        ObservationQueryResult result = await inner.QueryAsync(query, cancellationToken);
        while (MerelyLate(result.Metadata, query) && time.GetUtcNow() + settings.PollInterval <= deadline)
        {
            await Task.Delay(settings.PollInterval, time, cancellationToken);
            result = await inner.QueryAsync(query, cancellationToken);
        }

        return result;
    }

    private static bool MerelyLate(QueryMetadata metadata, ObservationQuery query) =>
        metadata.Completeness == QueryCompleteness.Complete && !metadata.Truncated && metadata.ObservedThrough < query.Range.To;
}
