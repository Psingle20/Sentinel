using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Ingestion.Application.Options;

/// <summary>Services whose traces to ingest. Only slow or failed traces are pulled, plus a small sample of the rest.</summary>
public sealed record TraceSourceDefinition
{
    public required string Name { get; init; }

    public required IReadOnlyList<string> Services { get; init; }

    public TimeSpan SlowerThan { get; init; } = TimeSpan.FromMilliseconds(500);

    public int SlowTraceLimit { get; init; } = 20;

    public int SampleTraceLimit { get; init; } = 3;
}
