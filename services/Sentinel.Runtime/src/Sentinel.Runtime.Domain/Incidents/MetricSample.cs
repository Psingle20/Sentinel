namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>One sample of a recovery signal.</summary>
public sealed record MetricSample(DateTimeOffset At, double Value);
