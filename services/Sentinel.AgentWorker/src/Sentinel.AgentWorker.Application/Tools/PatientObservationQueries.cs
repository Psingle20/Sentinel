using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Tools;

/// <summary>Configured as <c>Investigation:Completeness</c>.</summary>
public sealed class CompletenessOptions
{
    public const string Section = "Investigation:Completeness";

    /// <summary>The longest one query waits for its sources to catch up with the end of its range.</summary>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How far behind the end of the range an answer may be and still count as merely late. A source
    /// further behind is lagging, not late, and waiting for it would only add load.
    /// </summary>
    public TimeSpan MaxLag { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>The total waiting one investigation may spend across all its queries.</summary>
    public TimeSpan WaitBudget { get; set; } = TimeSpan.FromSeconds(45);
}

/// <summary>
/// Waits, briefly and boundedly, for data that is merely late (Phase 04, ADR-016). An investigation
/// starts seconds after detection, before logs and traces are indexed through the window's end, so
/// a first answer is often partial only because ingestion lags. When every source is healthy and
/// the answer is complete except that <c>ObservedThrough</c> trails the range by less than
/// <see cref="CompletenessOptions.MaxLag"/>, the query is asked again until it catches up, its own
/// wait runs out or the investigation's wait budget is spent; then the partial answer is returned
/// as partial. An unhealthy, truncated or far-behind answer is never waited for. One instance serves
/// one investigation scope, so the budget is per investigation.
/// </summary>
public sealed class PatientObservationQueries(IObservationQueries inner, Microsoft.Extensions.Options.IOptions<CompletenessOptions> options, TimeProvider time) : IObservationQueries
{
    private TimeSpan _waited;

    public async Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        CompletenessOptions settings = options.Value;
        ObservationQueryResult result = await inner.QueryAsync(query, cancellationToken);
        DateTimeOffset started = time.GetUtcNow();
        DateTimeOffset deadline = started + Min(settings.MaxWait, settings.WaitBudget - _waited);
        try
        {
            while (MerelyLate(result.Metadata, query, settings) && time.GetUtcNow() + settings.PollInterval <= deadline)
            {
                await Task.Delay(settings.PollInterval, time, cancellationToken);
                result = await inner.QueryAsync(query, cancellationToken);
            }
        }
        finally
        {
            _waited += time.GetUtcNow() - started;
        }

        return result;
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private static bool MerelyLate(QueryMetadata metadata, ObservationQuery query, CompletenessOptions settings) =>
        metadata.Completeness == QueryCompleteness.Complete
        && !metadata.Truncated
        && metadata.ObservedThrough < query.Range.To
        && query.Range.To - metadata.ObservedThrough <= settings.MaxLag;
}
