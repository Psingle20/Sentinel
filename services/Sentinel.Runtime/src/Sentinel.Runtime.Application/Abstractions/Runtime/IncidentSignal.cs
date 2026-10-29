namespace Sentinel.Runtime.Application.Abstractions.Runtime;

/// <summary>A hint to the incident's workflow that the stream changed; the workflow re-reads the incident to act on it.</summary>
public sealed record IncidentSignal(IncidentSignalKind Kind, string? Reference = null);
