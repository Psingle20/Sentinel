using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Abstractions.Providers;
using Sentinel.Runtime.Application.Incidents.InProcess;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Tests;

/// <summary>Answers recovery queries with a healthy, complete signal.</summary>
internal sealed class StubQueries : IObservationQueries
{
    public double Value { get; set; } = 0.2;

    /// <summary>The value before the recovery window, the pre-action baseline; defaults to <see cref="Value"/>.</summary>
    public double? Before { get; set; }

    public DateTimeOffset? WindowFrom { get; set; }

    public Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Five samples spread across the window, the last inside its final third, whatever its length.
        TimeSpan span = query.Range.To - query.Range.From;
        double value = Before is { } before && WindowFrom is { } from && query.Range.To <= from ? before : Value;
        MetricObservation[] samples = [.. Enumerable.Range(0, 5).Select(index => InMemoryRuntime.Burn(0, value) with
        {
            ObservationId = ObservationId.From($"prometheus:slo_burn_rate:checkout-service:recovery:{index}"),
            EventTime = query.Range.From + span * index / 5
        })];
        return Task.FromResult(new ObservationQueryResult(samples, new QueryMetadata
        {
            ObservedThrough = query.Range.To,
            Freshness = TimeSpan.Zero,
            Completeness = QueryCompleteness.Complete,
            Truncated = false,
            ProviderHealth = [],
            RedactionPolicyVersion = "test"
        }));
    }
}
