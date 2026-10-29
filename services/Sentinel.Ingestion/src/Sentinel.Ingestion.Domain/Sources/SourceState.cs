namespace Sentinel.Ingestion.Domain.Sources;

/// <summary>
/// Progress of one ingestion source, such as one metric series from one provider.
/// <see cref="Checkpoint"/> is where the next pull starts; <see cref="Watermark"/> is the event
/// time up to which this source's data is complete, which becomes a query's ObservedThrough.
/// </summary>
public sealed record SourceState
{
    public required string SourceId { get; init; }

    public required string Provider { get; init; }

    public required DateTimeOffset Checkpoint { get; init; }

    public required DateTimeOffset Watermark { get; init; }

    public required SourceHealth Health { get; init; }

    public string? LastError { get; init; }

    public int ConsecutiveFailures { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Longest wait between attempts while a source keeps failing.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The circuit breaker: after a failure the next attempt waits 5 s × 2^(failures − 1), capped at
    /// <see cref="MaxBackoff"/>, so an outage costs one bounded call per interval instead of one per cycle.
    /// The first failure is retried at once, because most are transient.
    /// </summary>
    public DateTimeOffset? NextAttemptAt => ConsecutiveFailures <= 1
        ? null
        : UpdatedAt + TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, TimeSpan.FromSeconds(5).Ticks << Math.Min(ConsecutiveFailures - 1, 10)));

    public bool IsCoolingDown(DateTimeOffset now) => NextAttemptAt is { } next && now < next;

    /// <summary>The state of a source seen for the first time: it starts <paramref name="lookback"/> in the past.</summary>
    public static SourceState Start(string sourceId, string provider, DateTimeOffset now, TimeSpan lookback) => new()
    {
        SourceId = sourceId,
        Provider = provider,
        Checkpoint = now - lookback,
        Watermark = now - lookback,
        Health = SourceHealth.Healthy,
        UpdatedAt = now
    };

    /// <summary>Failures in a row after which a source is reported unavailable rather than degraded.</summary>
    public const int UnavailableAfter = 3;

    /// <summary>
    /// A pull succeeded through <paramref name="through"/>: the checkpoint and the watermark move to it,
    /// never backwards, and the source recovers.
    /// </summary>
    public SourceState Advance(DateTimeOffset through, DateTimeOffset now) => (this with
    {
        Checkpoint = through > Checkpoint ? through : Checkpoint,
        Watermark = through > Watermark ? through : Watermark
    }).Recover(now);

    /// <summary>
    /// A pull failed. The checkpoint stays, so the range is pulled again; after
    /// <see cref="UnavailableAfter"/> failures in a row the source is unavailable rather than degraded.
    /// </summary>
    public SourceState MarkFailed(string error, DateTimeOffset now)
    {
        int failures = ConsecutiveFailures + 1;
        return MoveTo(failures >= UnavailableAfter ? SourceHealth.Unavailable : SourceHealth.Degraded, now) with
        {
            LastError = error,
            ConsecutiveFailures = failures
        };
    }

    /// <summary>The source answered: it is healthy, and its failure count and last error are cleared.</summary>
    public SourceState Recover(DateTimeOffset now) => MoveTo(SourceHealth.Healthy, now) with
    {
        LastError = null,
        ConsecutiveFailures = 0
    };

    // Every reachable state satisfies the table; a move outside it means a stored state was corrupted.
    private SourceState MoveTo(SourceHealth health, DateTimeOffset now) =>
        SourceHealthTransitions.CanMove(Health, health)
            ? this with { Health = health, UpdatedAt = now }
            : throw new InvalidOperationException($"Source {SourceId} cannot move from {Health} to {health}.");
}
