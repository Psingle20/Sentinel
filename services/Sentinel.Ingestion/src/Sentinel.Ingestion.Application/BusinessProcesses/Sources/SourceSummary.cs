namespace Sentinel.Ingestion.Application.BusinessProcesses.Sources;

/// <summary>
/// One source as operators see it: its provider, how far its data is complete and how its last pulls
/// went. <see cref="Health"/> is the health's name as written, for example <c>Degraded</c>. <see cref="LastPulledAt"/> is
/// when the source was last pulled, and <see cref="NextAttemptAt"/> when a failing source is next tried after its backoff.
/// </summary>
public sealed record SourceSummary(
    string SourceId,
    string Provider,
    DateTimeOffset Watermark,
    string Health,
    string? LastError,
    int ConsecutiveFailures,
    bool Healthy,
    DateTimeOffset LastPulledAt,
    DateTimeOffset? NextAttemptAt);
