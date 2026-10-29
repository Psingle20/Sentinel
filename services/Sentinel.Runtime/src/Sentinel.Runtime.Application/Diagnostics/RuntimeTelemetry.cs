using System.Diagnostics;
using System.Diagnostics.Metrics;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Application.Diagnostics;

/// <summary>
/// Runtime's incident telemetry (Phase 09), from which Sentinel's own SLOs are measured: how long detection, decision
/// and the recovery verdict take in each cycle, and what each decision, execution and recovery came to, by severity.
/// It is recorded from events once they are committed, so a refused or rolled-back command measures nothing.
/// </summary>
public sealed class RuntimeTelemetry : IDisposable
{
    public const string MeterName = "Sentinel.Runtime";

    private readonly Meter _meter = new(MeterName);

    public RuntimeTelemetry()
    {
        DetectionDelay = _meter.CreateHistogram("sentinel.runtime.incident.detection_delay", "s", "From a symptom's onset to its incident's detection or reopening.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = [5, 10, 15, 30, 60, 120, 300, 600] });
        TimeToDecision = _meter.CreateHistogram("sentinel.runtime.incident.time_to_decision", "s", "From the start of an incident's cycle to its decision, diagnosed or abstained.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = [30, 60, 120, 180, 300, 600, 900, 1800] });
        TimeToRecovery = _meter.CreateHistogram("sentinel.runtime.incident.time_to_recovery_verdict", "s", "From the start of an incident's cycle to the verdict on its recovery.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = [120, 300, 600, 900, 1800, 3600, 7200] });
        Decisions = _meter.CreateCounter<long>("sentinel.runtime.decisions", "{decision}", "Decisions made, by result.");
        Executions = _meter.CreateCounter<long>("sentinel.runtime.executions", "{execution}", "Execution results Executor reported, by result.");
        Recoveries = _meter.CreateCounter<long>("sentinel.runtime.recoveries", "{verdict}", "Recovery verdicts, by result.");
        Escalations = _meter.CreateCounter<long>("sentinel.runtime.escalations", "{incident}", "Incidents escalated to responders.");
    }

    public Histogram<double> DetectionDelay { get; }

    public Histogram<double> TimeToDecision { get; }

    public Histogram<double> TimeToRecovery { get; }

    public Counter<long> Decisions { get; }

    public Counter<long> Executions { get; }

    public Counter<long> Recoveries { get; }

    public Counter<long> Escalations { get; }

    /// <summary>Records what the events an incident has just committed measure.</summary>
    public void Committed(Incident incident, IReadOnlyList<StoredEvent> appended)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(appended);

        var severity = new KeyValuePair<string, object?>("sentinel.severity", incident.Severity.ToString());
        foreach (StoredEvent stored in appended)
        {
            switch (stored.Event)
            {
                case IncidentDetected detected:
                    DetectionDelay.Record(Seconds(detected.OccurredAt - detected.SymptomOnset), severity);
                    break;
                case IncidentReopened reopened:
                    DetectionDelay.Record(Seconds(reopened.OccurredAt - reopened.SymptomOnset), severity);
                    break;
                case DecisionMade made:
                    var decision = new TagList { severity, { "sentinel.decision", made.Decision.Result.ToString() } };
                    TimeToDecision.Record(Seconds(made.OccurredAt - incident.CycleStartedAt), decision);
                    Decisions.Add(1, decision);
                    break;
                case ExecutionReported reported:
                    Executions.Add(1, severity, new KeyValuePair<string, object?>("sentinel.execution", reported.Result.ToString()));
                    break;
                case RecoveryEvaluated evaluated:
                    var recovery = new TagList { severity, { "sentinel.recovery", evaluated.Result.ToString() } };
                    TimeToRecovery.Record(Seconds(evaluated.OccurredAt - incident.CycleStartedAt), recovery);
                    Recoveries.Add(1, recovery);
                    break;
                case IncidentEscalated:
                    Escalations.Add(1, severity);
                    break;
            }
        }
    }

    public void Dispose() => _meter.Dispose();

    private static double Seconds(TimeSpan span) => Math.Max(0, span.TotalSeconds);
}
