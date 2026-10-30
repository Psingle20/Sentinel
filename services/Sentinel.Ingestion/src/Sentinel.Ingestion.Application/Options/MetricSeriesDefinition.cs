using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.Options;

/// <summary>
/// One metric series to ingest. <see cref="Expression"/> is the provider's own query language and
/// stays in configuration; models never write it (ADR-007). The series' service is either fixed
/// (<see cref="Service"/>) or read from a label on each returned series (<see cref="ServiceLabel"/>).
/// </summary>
public sealed record MetricSeriesDefinition
{
    public required string Name { get; init; }

    public required string Expression { get; init; }

    public string? Service { get; init; }

    public string? ServiceLabel { get; init; }

    public string? Unit { get; init; }

    public string? Aggregation { get; init; }

    public TimeSpan? Window { get; init; }

    /// <summary>Higher-priority series are pulled first in each cycle (Phase 02 stream prioritization).</summary>
    public int Priority { get; init; }

    /// <summary>
    /// The metrics provider that serves the series, by name: Prometheus when unset, or <c>sqs</c> for SQS queue attributes in
    /// the AWS-emulated environment (Phase E01). <see cref="Expression"/> is in that provider's own language.
    /// </summary>
    public string? Provider { get; init; }
}
