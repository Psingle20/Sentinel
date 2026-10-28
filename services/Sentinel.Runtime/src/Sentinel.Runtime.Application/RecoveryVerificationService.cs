using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application;

/// <summary>What recovery verification found; untrustworthy answers are retried until the deadline.</summary>
public sealed record RecoveryAssessment(bool Trustworthy, RecoveryResult Result, IReadOnlyList<Measurement> Measurements, DateTimeOffset ObservedThrough, string? Reason);

/// <summary>
/// Verify: after the plan's observation window, query each expected recovery signal through
/// Ingestion and compare it with its threshold. The answer is inconclusive, never recovered, unless
/// the query's ObservedThrough covers the whole window and the result is complete and untruncated
/// (ADR-007). The workflow's durable timer decides when this runs and how long it may retry.
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

        var measurements = new List<Measurement>();
        var problems = new List<string>();
        DateTimeOffset observedThrough = to;
        bool improvedOrFlat = true;
        bool worseThanBefore = false;

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
            if (metadata.ObservedThrough < to || metadata.Completeness != QueryCompleteness.Complete || metadata.Truncated)
            {
                problems.Add($"{criterion.MetricName} observed through {metadata.ObservedThrough:O}, {metadata.Completeness}{(metadata.Truncated ? ", truncated" : string.Empty)}");
                measurements.Add(Measure(criterion, null, null));
                continue;
            }

            MetricObservation[] samples = [.. answer.Observations.OfType<MetricObservation>().OrderBy(sample => sample.EventTime)];
            if (samples.Length == 0)
            {
                problems.Add($"{criterion.MetricName} has no samples in the window");
                measurements.Add(Measure(criterion, null, null));
                continue;
            }

            // Recovered means the signal holds through the final third of the window, not one good sample.
            DateTimeOffset tailFrom = to - (to - from) / 3;
            MetricObservation[] tail = [.. samples.Where(sample => sample.EventTime >= tailFrom)];
            bool met = tail.Length > 0 && tail.All(sample => Meets(criterion, sample.Value));
            double last = samples[^1].Value;
            measurements.Add(Measure(criterion, last, met));
            improvedOrFlat &= !Worse(criterion, samples[0].Value, last);

            // The pre-action baseline: the same signal over an equal window before the action (Phase 06).
            if (!met && await BaselineAsync(incident, criterion, from, to - from, cancellationToken) is { } before && tail.Length > 0)
            {
                worseThanBefore |= Worse(criterion, before, tail.Average(sample => sample.Value));
            }
        }

        if (incident.ExpectedRecovery.Count == 0)
        {
            problems.Add("the plan states no recovery criteria");
        }

        if (problems.Count > 0)
        {
            return new RecoveryAssessment(false, RecoveryResult.Inconclusive, measurements, observedThrough, string.Join("; ", problems));
        }

        if (measurements.TrueForAll(measurement => measurement.Met == true))
        {
            return new RecoveryAssessment(true, RecoveryResult.Recovered, measurements, observedThrough, null);
        }

        string unmet = string.Join(", ", measurements.Where(measurement => measurement.Met != true).Select(measurement => $"{measurement.MetricName}={measurement.ObservedValue:G4}"));
        if (worseThanBefore)
        {
            // The system is worse after the action than before it: the action must be undone or overridden.
            return new RecoveryAssessment(true, RecoveryResult.CompensationRequired, measurements, observedThrough, $"Signals are worse than before the action: {unmet}");
        }

        RecoveryResult result = improvedOrFlat ? RecoveryResult.Unchanged : RecoveryResult.Degraded;
        return new RecoveryAssessment(true, result, measurements, observedThrough, $"Expected signals not restored: {unmet}");
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
        MetricObservation[] samples = [.. before.Observations.OfType<MetricObservation>()];
        return samples.Length == 0 ? null : samples.Average(sample => sample.Value);
    }

    private static Measurement Measure(RecoveryCriterion criterion, double? value, bool? met) =>
        new(criterion.ServiceId.Value, criterion.MetricName, criterion.Comparison.ToString(), criterion.Threshold, value, met);

    private static bool Meets(RecoveryCriterion criterion, double value) => criterion.Comparison switch
    {
        ThresholdComparison.LessThan => value < criterion.Threshold,
        ThresholdComparison.LessThanOrEqual => value <= criterion.Threshold,
        ThresholdComparison.GreaterThan => value > criterion.Threshold,
        _ => value >= criterion.Threshold
    };

    private static bool Worse(RecoveryCriterion criterion, double first, double last) =>
        criterion.Comparison is ThresholdComparison.LessThan or ThresholdComparison.LessThanOrEqual
            ? last > first * 1.1
            : last < first * 0.9;
}

/// <summary>
/// Incident queries served to Api. Lists and details come from the read model; the authoritative
/// state comes from the incident's writer, so a caller can see how far the projection lags.
/// </summary>
public sealed class IncidentQueryService(IIncidentStore incidents, IIncidentOwner owner)
{
    public Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken) =>
        incidents.ListAsync(Math.Clamp(limit, 1, 200), cancellationToken);

    public async Task<Result<IncidentDetail>> GetAsync(Sentinel.Contracts.Primitives.Identifiers.IncidentId incidentId, CancellationToken cancellationToken) =>
        await incidents.GetDetailAsync(incidentId, cancellationToken) is { } detail
            ? Result.Success(detail)
            : Result.Failure<IncidentDetail>(IncidentCommandProcessor.NotFound);


    /// <summary>The authoritative state beside the projection's version: projection lag made visible (Phase 03).</summary>
    public async Task<Result<IncidentStateView>> StateAsync(Sentinel.Contracts.Primitives.Identifiers.IncidentId incidentId, CancellationToken cancellationToken)
    {
        IncidentSnapshot? snapshot = await owner.GetAsync(incidentId, cancellationToken);
        if (snapshot is null)
        {
            return Result.Failure<IncidentStateView>(IncidentCommandProcessor.NotFound);
        }

        long? projected = await incidents.ProjectionVersionAsync(incidentId, cancellationToken);
        return Result.Success(new IncidentStateView(
            snapshot.IncidentId,
            snapshot.State.ToStatus(),
            snapshot.Version,
            projected,
            projected is { } version ? snapshot.Version - version : null,
            snapshot.Cycle,
            snapshot.PendingInvestigationIds));
    }
}
