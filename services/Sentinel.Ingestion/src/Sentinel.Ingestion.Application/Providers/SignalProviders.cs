using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Ingestion.Application.Providers;

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

/// <summary>Services whose traces to ingest. Only slow or failed traces are pulled, plus a small sample of the rest.</summary>
public sealed record TraceSourceDefinition
{
    public required string Name { get; init; }

    public required IReadOnlyList<string> Services { get; init; }

    public TimeSpan SlowerThan { get; init; } = TimeSpan.FromMilliseconds(500);

    public int SlowTraceLimit { get; init; } = 20;

    public int SampleTraceLimit { get; init; } = 3;
}

public sealed record LogRangeRequest(TenantId TenantId, EnvironmentId EnvironmentId, LogSourceDefinition Source, TimeRange Range);

public sealed record TraceRangeRequest(TenantId TenantId, EnvironmentId EnvironmentId, TraceSourceDefinition Source, TimeRange Range);

public sealed record TopologyRequest(TenantId TenantId, EnvironmentId EnvironmentId, TimeRange Range);

public interface ILogsProvider
{
    string Name { get; }

    Task<ProviderResult<LogObservation>> QueryRangeAsync(LogRangeRequest request, CancellationToken cancellationToken);
}

public interface ITracesProvider
{
    string Name { get; }

    Task<ProviderResult<TraceObservation>> QueryRangeAsync(TraceRangeRequest request, CancellationToken cancellationToken);
}

public interface ITopologyProvider
{
    string Name { get; }

    Task<ProviderResult<TopologyObservation>> GetDependenciesAsync(TopologyRequest request, CancellationToken cancellationToken);
}
