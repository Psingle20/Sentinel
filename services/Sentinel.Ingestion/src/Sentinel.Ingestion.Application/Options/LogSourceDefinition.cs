using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Ingestion.Application.Options;

/// <summary>One log stream to ingest, in the provider's query language (configuration only, ADR-007).</summary>
public sealed record LogSourceDefinition
{
    public required string Name { get; init; }

    public required string Query { get; init; }

    /// <summary>The label naming the service on each returned stream, for example <c>service_name</c>.</summary>
    public string ServiceLabel { get; init; } = "service_name";

    public int Limit { get; init; } = 500;

    public int Priority { get; init; }
}
