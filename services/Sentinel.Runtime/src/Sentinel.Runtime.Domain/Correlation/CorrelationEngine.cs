using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Domain.Correlation.Decisions;

namespace Sentinel.Runtime.Domain.Correlation;

/// <summary>
/// The correlation engine (Phase 03): decides, with explainable reasons, whether a firing signal
/// opens an incident, merges into a related open one, reopens a recent one or is suppressed, and
/// scores which recent changes to link. Unrelated simultaneous failures stay separate incidents.
/// Pure and deterministic: the same inputs give the same decision.
/// </summary>
public static class CorrelationEngine
{
    public static CorrelationDecision Correlate(
        CorrelationSignal signal,
        IReadOnlyList<IncidentContext> incidents,
        IReadOnlyList<LinkedChange> recentChanges,
        ServiceTopology topology,
        IReadOnlyList<Suppression> suppressions,
        CorrelationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(incidents);
        ArgumentNullException.ThrowIfNull(recentChanges);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(suppressions);
        ArgumentNullException.ThrowIfNull(settings);

        if (suppressions.FirstOrDefault(rule => rule.Matches(signal.ServiceId, signal.MetricName, signal.EventTime)) is { } suppression)
        {
            return new SuppressSignal([$"Suppressed until {suppression.Until:O}: {suppression.Reason}"]);
        }

        if (incidents.FirstOrDefault(incident => incident.IsOpen && incident.AffectedServices.Contains(signal.ServiceId)) is { } own)
        {
            return new AbsorbIntoIncident(own.IncidentId, [$"{signal.ServiceId} already has open incident {own.IncidentId}."]);
        }

        // A related service's symptom that began close to an open incident's onset is the same failure.
        IncidentContext? related = incidents
            .Where(incident => incident.IsOpen && (signal.Onset - incident.SymptomOnset).Duration() <= settings.MergeWindow)
            .Select(incident => (Incident: incident, Distance: incident.AffectedServices.Select(service => topology.Distance(service, signal.ServiceId)).Min()))
            .Where(candidate => candidate.Distance is { } hops && hops <= settings.MaxMergeDistance)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => (signal.Onset - candidate.Incident.SymptomOnset).Duration())
            .Select(candidate => candidate.Incident)
            .FirstOrDefault();
        if (related is not null)
        {
            TimeSpan apart = (signal.Onset - related.SymptomOnset).Duration();
            double score = Math.Round(Proximity(apart, settings) * TopologyWeight(topology.Distance(related.AffectedServiceId, signal.ServiceId)), 3);
            return new MergeIntoIncident(related.IncidentId, score,
            [
                string.Create(CultureInfo.InvariantCulture, $"{signal.ServiceId} is a dependency neighbour of {related.AffectedServiceId}."),
                string.Create(CultureInfo.InvariantCulture, $"Its symptom began {apart.TotalMinutes:F1} min from the incident's onset, inside the {settings.MergeWindow.TotalMinutes:F0} min merge window.")
            ]);
        }

        // The same symptom shortly after resolution reopens the incident, unless a new change could explain it.
        IncidentContext? recent = incidents
            .Where(incident => !incident.IsOpen && incident.AffectedServiceId == signal.ServiceId && incident.ResolvedAt is { } resolved && signal.Onset - resolved <= settings.ReopenWindow && signal.Onset >= resolved)
            .OrderByDescending(incident => incident.ResolvedAt)
            .FirstOrDefault();
        if (recent is not null)
        {
            LinkedChange? newChange = recentChanges.FirstOrDefault(change => change.StartedAt >= recent.ResolvedAt && topology.Distance(change.ServiceId, signal.ServiceId) is <= 1);
            if (newChange is null)
            {
                return new ReopenIncident(recent.IncidentId,
                [
                    string.Create(CultureInfo.InvariantCulture, $"{signal.ServiceId} {signal.MetricName} returned {(signal.Onset - recent.ResolvedAt!.Value).TotalMinutes:F1} min after incident {recent.IncidentId} was resolved, with no new change.")
                ]);
            }
        }

        IReadOnlyList<LinkedChange> changes = ScoreChanges(signal.ServiceId, signal.Onset, recentChanges, topology, settings);
        return new OpenIncident(changes,
        [
            string.Create(CultureInfo.InvariantCulture, $"{signal.ServiceId} {signal.MetricName} at {signal.Value:F1} has no related open incident."),
            string.Create(CultureInfo.InvariantCulture, $"{changes.Count} of {recentChanges.Count} recent change(s) scored at least {settings.MinimumChangeScore:F2}.")
        ]);
    }

    /// <summary>
    /// Scores each change by topology distance from the affected service, temporal proximity to the
    /// onset and change kind, keeps those above the threshold, and records why, highest first.
    /// </summary>
    public static IReadOnlyList<LinkedChange> ScoreChanges(ServiceId service, DateTimeOffset onset, IReadOnlyList<LinkedChange> changes, ServiceTopology topology, CorrelationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(settings);

        var scored = new List<LinkedChange>();
        foreach (LinkedChange change in changes)
        {
            int? distance = topology.Distance(service, change.ServiceId);
            DateTimeOffset effective = change.CompletedAt ?? change.StartedAt;
            TimeSpan before = onset - effective;
            // A change that started serving only after onset can matter only while it was rolling out.
            double proximity = change.StartedAt <= onset ? 0.6 : 0.15;
            if (before >= TimeSpan.Zero)
            {
                proximity = Proximity(before, settings);
            }

            double score = Math.Round(proximity * TopologyWeight(distance) * KindWeight(change.ChangeKind), 3);
            if (score < settings.MinimumChangeScore)
            {
                continue;
            }

            scored.Add(change with
            {
                CorrelationScore = score,
                CorrelationReasons =
                [
                    distance switch
                    {
                        0 => $"same service as the symptom ({service})",
                        1 => $"direct dependency neighbour of {service}",
                        { } hops => string.Create(CultureInfo.InvariantCulture, $"{hops} dependency hops from {service}"),
                        _ => $"no known dependency path to {service}"
                    },
                    before >= TimeSpan.Zero
                        ? string.Create(CultureInfo.InvariantCulture, $"serving {before.TotalMinutes:F1} min before onset")
                        : string.Create(CultureInfo.InvariantCulture, $"not serving until {(-before).TotalMinutes:F1} min after onset"),
                    $"{change.ChangeKind} {change.Version}"
                ]
            });
        }

        return [.. scored.OrderByDescending(change => change.CorrelationScore).ThenBy(change => change.StartedAt)];
    }

    private static double Proximity(TimeSpan apart, CorrelationSettings settings) =>
        Math.Exp(-apart.TotalSeconds / settings.ProximityScale.TotalSeconds);

    private static double TopologyWeight(int? distance) => distance switch
    {
        0 => 1.0,
        1 => 0.7,
        2 => 0.4,
        _ => 0.1
    };

    private static double KindWeight(string kind) => kind switch
    {
        "deploy" or "rollback" => 1.0,
        "config-change" => 0.9,
        _ => 0.3
    };
}
