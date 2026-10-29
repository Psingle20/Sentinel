namespace Sentinel.Ingestion.Domain.Sources;

/// <summary>
/// The moves a source's health may make, as a table. A source is healthy until a pull fails, degraded
/// while failures accumulate and unavailable once they reach <see cref="SourceState.UnavailableAfter"/>;
/// any successful pull makes it healthy again. It never goes from healthy straight to unavailable, nor
/// from unavailable back to degraded. <see cref="SourceState"/> checks this table and never compares
/// its health inline.
/// </summary>
public static class SourceHealthTransitions
{
    private static readonly Dictionary<SourceHealth, SourceHealth[]> Transitions = new()
    {
        [SourceHealth.Healthy] = [SourceHealth.Degraded],
        [SourceHealth.Degraded] = [SourceHealth.Healthy, SourceHealth.Unavailable],
        [SourceHealth.Unavailable] = [SourceHealth.Healthy]
    };

    /// <summary>Whether a source in <paramref name="from"/> may move to <paramref name="to"/>.</summary>
    public static bool CanMove(SourceHealth from, SourceHealth to) => from == to || Transitions[from].Contains(to);
}
