namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// The recovery rule (Phase 06): every expected signal must meet its threshold through the final
/// third of the window, not in one good sample. Telemetry that does not cover the window makes the
/// verdict inconclusive, never recovered (ADR-007). A signal worse after the action than before it
/// requires compensation; otherwise an unmet window is unchanged or, if a signal worsened, degraded.
/// </summary>
public static class RecoveryCriteria
{
    public static RecoveryJudgement Judge(DateTimeOffset windowFrom, DateTimeOffset windowTo, IReadOnlyList<SignalReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        var measurements = new List<Measurement>();
        var problems = new List<string>();
        bool improvedOrFlat = true;
        bool worseThanBefore = false;

        foreach (SignalReading reading in readings)
        {
            if (reading.CoverageProblem is { } coverage)
            {
                problems.Add(coverage);
                measurements.Add(Measure(reading, null, null));
                continue;
            }

            MetricSample[] samples = [.. reading.Samples.OrderBy(sample => sample.At)];
            if (samples.Length == 0)
            {
                problems.Add($"{reading.MetricName} has no samples in the window");
                measurements.Add(Measure(reading, null, null));
                continue;
            }

            DateTimeOffset tailFrom = windowTo - (windowTo - windowFrom) / 3;
            MetricSample[] tail = [.. samples.Where(sample => sample.At >= tailFrom)];
            bool met = tail.Length > 0 && tail.All(sample => Meets(reading, sample.Value));
            double last = samples[^1].Value;

            measurements.Add(Measure(reading, last, met, reading.Baseline));
            improvedOrFlat &= !Worse(reading, samples[0].Value, last);
            if (!met && reading.Baseline is { } baseline && tail.Length > 0)
            {
                worseThanBefore |= Worse(reading, baseline, tail.Average(sample => sample.Value));
            }
        }

        if (readings.Count == 0)
        {
            problems.Add("the plan states no recovery criteria");
        }

        if (problems.Count > 0)
        {
            return new RecoveryJudgement(false, RecoveryResult.Inconclusive, measurements, string.Join("; ", problems));
        }

        if (measurements.TrueForAll(measurement => measurement.Met == true))
        {
            return new RecoveryJudgement(true, RecoveryResult.Recovered, measurements, null);
        }

        string unmet = string.Join(", ", measurements.Where(measurement => measurement.Met != true).Select(measurement => $"{measurement.MetricName}={measurement.ObservedValue:G4}"));
        if (worseThanBefore)
        {
            // The system is worse after the action than before it: the action must be undone or overridden.
            return new RecoveryJudgement(true, RecoveryResult.CompensationRequired, measurements, $"Signals are worse than before the action: {unmet}");
        }

        return new RecoveryJudgement(true, improvedOrFlat ? RecoveryResult.Unchanged : RecoveryResult.Degraded, measurements, $"Expected signals not restored: {unmet}");
    }

    private static Measurement Measure(SignalReading reading, double? value, bool? met, double? baseline = null) =>
        new(reading.ServiceId, reading.MetricName, reading.Comparison.ToString(), reading.Threshold, value, met, baseline);

    private static bool Meets(SignalReading reading, double value) => reading.Comparison switch
    {
        RecoveryComparison.LessThan => value < reading.Threshold,
        RecoveryComparison.LessThanOrEqual => value <= reading.Threshold,
        RecoveryComparison.GreaterThan => value > reading.Threshold,
        _ => value >= reading.Threshold
    };

    private static bool Worse(SignalReading reading, double first, double last) =>
        reading.Comparison is RecoveryComparison.LessThan or RecoveryComparison.LessThanOrEqual
            ? last > first * 1.1
            : last < first * 0.9;
}
