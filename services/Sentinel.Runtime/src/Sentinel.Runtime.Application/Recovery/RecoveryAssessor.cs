using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Providers;

namespace Sentinel.Runtime.Application.Recovery;

/// <summary>
/// Verify: after the plan's observation window, query each expected recovery signal through
/// Ingestion, with its baseline before the action, and note where the telemetry does not cover the
/// window completely (ADR-007); <see cref="RecoveryCriteria"/> judges what was read. The workflow's
/// durable timer decides when this runs and how long it may retry.
/// </summary>
public sealed class RecoveryAssessor(IObservationQueries observations)
{
    public async Task<RecoveryAssessment> AssessAsync(IncidentSnapshot incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        if (incident.RecoveryWindowFrom is not { } from || incident.RecoveryWindowTo is not { } to)
        {
            return new RecoveryAssessment(false, RecoveryResult.Inconclusive, [], incident.SymptomOnset, "No recovery window is scheduled.");
        }

        var readings = new List<SignalReading>();
        DateTimeOffset observedThrough = to;
        foreach (RecoveryCriterion criterion in incident.ExpectedRecovery)
        {
            ObservationQueryResult answer = await observations.QueryAsync(new ObservationQuery
            {
                TenantId = incident.TenantId,
                EnvironmentId = incident.EnvironmentId,
                ServiceId = criterion.ServiceId,
                Kind = ObservationKind.Metric,
                Range = new TimeRange(from, to),
                Filters = new Dictionary<string, string> { ["metricName"] = criterion.MetricName },
                Limit = 1000
            }, cancellationToken);

            QueryMetadata metadata = answer.Metadata;
            observedThrough = metadata.ObservedThrough < observedThrough ? metadata.ObservedThrough : observedThrough;
            RecoveryComparison comparison = Enum.Parse<RecoveryComparison>(criterion.Comparison.ToString());
            if (metadata.ObservedThrough < to || metadata.Completeness != QueryCompleteness.Complete || metadata.Truncated)
            {
                string coverage = $"{criterion.MetricName} observed through {metadata.ObservedThrough:O}, {metadata.Completeness}{(metadata.Truncated ? ", truncated" : string.Empty)}";
                readings.Add(new SignalReading(criterion.ServiceId.Value, criterion.MetricName, comparison, criterion.Threshold, [], null, coverage));
                continue;
            }

            // The pre-action baseline: the same signal over an equal window before the action (Phase 06),
            // recorded with every measurement so responders see before and after (Phase 08).
            MetricSample[] samples = [.. answer.Observations.OfType<MetricObservation>().Select(sample => new MetricSample(sample.EventTime, sample.Value))];
            double? before = samples.Length == 0 ? null : await BaselineAsync(incident, criterion, from, to - from, cancellationToken);
            readings.Add(new SignalReading(criterion.ServiceId.Value, criterion.MetricName, comparison, criterion.Threshold, samples, before, null));
        }

        RecoveryJudgement judgement = RecoveryCriteria.Judge(from, to, readings);
        return new RecoveryAssessment(judgement.Trustworthy, judgement.Result, judgement.Measurements, observedThrough, judgement.Reason);
    }

    private async Task<double?> BaselineAsync(IncidentSnapshot incident, RecoveryCriterion criterion, DateTimeOffset actionAt, TimeSpan length, CancellationToken cancellationToken)
    {
        ObservationQueryResult before = await observations.QueryAsync(new ObservationQuery
        {
            TenantId = incident.TenantId,
            EnvironmentId = incident.EnvironmentId,
            ServiceId = criterion.ServiceId,
            Kind = ObservationKind.Metric,
            Range = new TimeRange(actionAt - length, actionAt),
            Filters = new Dictionary<string, string> { ["metricName"] = criterion.MetricName },
            Limit = 1000
        }, cancellationToken);

        // A baseline the telemetry does not cover completely could make recovery look worse than it is and ask for
        // compensation on a partial picture, so it counts as no baseline and the thresholds alone decide.
        QueryMetadata coverage = before.Metadata;
        if (coverage.ObservedThrough < actionAt || coverage.Completeness != QueryCompleteness.Complete || coverage.Truncated)
        {
            return null;
        }

        MetricObservation[] samples = [.. before.Observations.OfType<MetricObservation>()];
        return samples.Length == 0 ? null : samples.Average(sample => sample.Value);
    }
}
