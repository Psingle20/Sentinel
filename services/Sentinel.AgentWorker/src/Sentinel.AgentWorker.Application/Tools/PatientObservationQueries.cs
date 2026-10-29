using Sentinel.Contracts.Queries;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Options;

namespace Sentinel.AgentWorker.Application.Tools;

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
