using System.Diagnostics.Metrics;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Diagnostics;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Domain.Incidents.Events;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;

namespace Sentinel.Runtime.Tests;

/// <summary>The incident timings and outcomes Sentinel's own SLOs are measured from (Phase 09).</summary>
public sealed class RuntimeTelemetryTests
{
    [Fact]
    public void Committed_events_measure_detection_decision_and_the_recovery_verdict_from_the_cycle_start()
    {
        // Detected at 70 s for a symptom from 60 s; decided at 190 s; recovery judged at 430 s.
        (Incident incident, _) = Detected(onsetAt: 60);
        StoredEvent[] detected = Pending(incident);
        using var telemetry = new RuntimeTelemetry();
        using var recorded = new Recorded(telemetry);

        telemetry.Committed(incident, [
            .. detected,
            new StoredEvent(detected.Length + 1, new DecisionMade(Decision(DecisionResult.Diagnosed), Start.AddSeconds(190))),
            new StoredEvent(detected.Length + 2, new ExecutionReported(PlanId.New(), 1, ExecutionResult.ActionVerified, null, Start.AddSeconds(300))),
            new StoredEvent(detected.Length + 3, new RecoveryEvaluated(PlanId.New(), RecoveryResult.Recovered, [], Start.AddSeconds(420), null, Start.AddSeconds(430)))
        ]);

        recorded.Single("sentinel.runtime.incident.detection_delay").ShouldBe((10, "sentinel.severity=Critical"));
        recorded.Single("sentinel.runtime.incident.time_to_decision").ShouldBe((120, "sentinel.decision=Diagnosed;sentinel.severity=Critical"));
        recorded.Single("sentinel.runtime.decisions").ShouldBe((1, "sentinel.decision=Diagnosed;sentinel.severity=Critical"));
        recorded.Single("sentinel.runtime.executions").ShouldBe((1, "sentinel.execution=ActionVerified;sentinel.severity=Critical"));
        recorded.Single("sentinel.runtime.incident.time_to_recovery_verdict").ShouldBe((360, "sentinel.recovery=Recovered;sentinel.severity=Critical"));
        recorded.Single("sentinel.runtime.recoveries").ShouldBe((1, "sentinel.recovery=Recovered;sentinel.severity=Critical"));
    }

    [Fact]
    public void A_reopened_cycle_is_timed_from_its_reopening_not_the_first_detection()
    {
        (Incident first, _) = Detected(onsetAt: 60);
        var reopened = new IncidentReopened("The symptom returned.", [], Start.AddSeconds(900), Start.AddSeconds(930));
        var incident = Incident.Rehydrate([.. Pending(first).Select(stored => stored.Event), reopened]);
        using var telemetry = new RuntimeTelemetry();
        using var recorded = new Recorded(telemetry);

        telemetry.Committed(incident, [
            new StoredEvent(incident.Version, reopened),
            new StoredEvent(incident.Version + 1, new DecisionMade(Decision(DecisionResult.Abstained), Start.AddSeconds(1_000)))
        ]);

        incident.CycleStartedAt.ShouldBe(Start.AddSeconds(930));
        recorded.Single("sentinel.runtime.incident.detection_delay").Value.ShouldBe(30);
        recorded.Single("sentinel.runtime.incident.time_to_decision").ShouldBe((70, "sentinel.decision=Abstained;sentinel.severity=Critical"));
    }

    private static StoredEvent[] Pending(Incident incident)
    {
        IIncidentEvent[] pending = [.. incident.DequeueDomainEvents().Cast<IIncidentEvent>()];
        long expected = incident.Version - pending.Length;
        return [.. pending.Select((@event, index) => new StoredEvent(expected + index + 1, @event))];
    }

    private static Decision Decision(DecisionResult result) => new()
    {
        DecisionId = DecisionId.New(),
        Result = result,
        Confidence = 0.8,
        EvidenceCompleteness = 1,
        Rationale = "The deployment caused the symptoms.",
        DecidedAt = Start
    };

    /// <summary>Every measurement of one telemetry instance while it lives, with its tags in name order; tests running alongside use their own.</summary>
    private sealed class Recorded : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, double Value, string Tags)> _measurements = [];

        public Recorded(RuntimeTelemetry telemetry)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, telemetry.Decisions.Meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument.Name, value, tags));
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument.Name, value, tags));
            _listener.Start();
        }

        public (double Value, string Tags) Single(string name)
        {
            lock (_measurements)
            {
                (string _, double value, string tags) = _measurements.Single(measurement => measurement.Name == name);
                return (value, tags);
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string joined = string.Join(";", tags.ToArray().OrderBy(tag => tag.Key, StringComparer.Ordinal).Select(tag => $"{tag.Key}={tag.Value}"));
            lock (_measurements)
            {
                _measurements.Add((name, value, joined));
            }
        }
    }
}
