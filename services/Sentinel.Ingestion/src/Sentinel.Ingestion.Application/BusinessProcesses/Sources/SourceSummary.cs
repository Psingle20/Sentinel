namespace Sentinel.Ingestion.Application.BusinessProcesses.Sources;

/// <summary>
/// One source as operators see it: its provider, how far its data is complete and how its last pulls
/// went. <see cref="Health"/> is the health's name as written, for example <c>Degraded</c>.
/// </summary>
public sealed record SourceSummary(
    string SourceId,
    string Provider,
    DateTimeOffset Watermark,
    string Health,
    string? LastError,
    int ConsecutiveFailures,
    bool Healthy);
