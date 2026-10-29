using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Runtime.Application.Abstractions.Providers;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Recovery;
using Sentinel.Runtime.Domain.Incidents;
using Shouldly;

namespace Sentinel.Runtime.Tests;

/// <summary>Phase 06 recovery verification over Ingestion's query contract: what the telemetry did and did not cover.</summary>
public sealed class RecoveryAssessorTests
{
    private static readonly DateTimeOffset ActionAt = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = ActionAt.AddMinutes(2);
    private static readonly ServiceId Checkout = ServiceId.From("checkout-service");

    [Fact]
    public async Task A_complete_baseline_lower_than_the_unmet_window_asks_for_compensation()
    {
        RecoveryAssessment assessment = await new RecoveryAssessor(new ScriptedQueries(baselineComplete: true)).AssessAsync(Incident(), CancellationToken.None);

        assessment.Result.ShouldBe(RecoveryResult.CompensationRequired);
        assessment.Measurements.ShouldHaveSingleItem().BaselineValue.ShouldBe(0.5);
    }

    [Fact]
    public async Task A_truncated_or_partial_baseline_counts_as_no_baseline_and_never_asks_for_compensation()
    {
        RecoveryAssessment assessment = await new RecoveryAssessor(new ScriptedQueries(baselineComplete: false)).AssessAsync(Incident(), CancellationToken.None);

        assessment.Result.ShouldNotBe(RecoveryResult.CompensationRequired);
        assessment.Result.ShouldNotBe(RecoveryResult.Recovered);
        assessment.Measurements.ShouldHaveSingleItem().BaselineValue.ShouldBeNull();
    }

    private static IncidentSnapshot Incident() => new()
    {
        IncidentId = IncidentId.From(Guid.NewGuid()),
        TenantId = InMemoryRuntime.Tenant,
        EnvironmentId = InMemoryRuntime.Environment,
        AffectedServiceId = Checkout,
        Title = "Checkout success SLO burning",
        State = IncidentState.Remediating,
        Severity = IncidentSeverity.High,
        Version = 10,
        Cycle = 0,
        SymptomOnset = ActionAt.AddMinutes(-5),
        ExpectedRecovery = [new RecoveryCriterion { ServiceId = Checkout, MetricName = "slo_burn_rate", Comparison = ThresholdComparison.LessThan, Threshold = 1 }],
        RecoveryWindowFrom = ActionAt,
        RecoveryWindowTo = WindowEnd
    };

    /// <summary>The window after the action stays above the threshold; the baseline before it was low, complete or not.</summary>
    private sealed class ScriptedQueries(bool baselineComplete) : IObservationQueries
    {
        public Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
        {
            bool baseline = query.Range.To <= ActionAt;
            double value = baseline ? 0.5 : 4;
            Observation[] samples = [.. Enumerable.Range(0, 12).Select(index => Sample(query.Range.From.AddSeconds(index * 10), value))];
            return Task.FromResult(new ObservationQueryResult(samples, new QueryMetadata
            {
                ObservedThrough = query.Range.To,
                Freshness = TimeSpan.FromSeconds(5),
                Completeness = baseline && !baselineComplete ? QueryCompleteness.Partial : QueryCompleteness.Complete,
                Truncated = baseline && !baselineComplete,
                ProviderHealth = [],
                RedactionPolicyVersion = "redaction-v1"
            }));
        }

        private static MetricObservation Sample(DateTimeOffset at, double value) => new()
        {
            ObservationId = ObservationId.From($"prometheus:slo_burn_rate:checkout-service:{at.ToUnixTimeSeconds()}"),
            TenantId = InMemoryRuntime.Tenant,
            EnvironmentId = InMemoryRuntime.Environment,
            ServiceId = Checkout,
            SourceProvider = "prometheus",
            SourceResource = "slo_burn_rate",
            EventTime = at,
            IngestTime = at.AddSeconds(5),
            MetricName = "slo_burn_rate",
            Value = value
        };
    }
}
