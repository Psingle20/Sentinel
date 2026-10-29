using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation;

/// <summary>Tunable correlation rules (Phase 03), configured as <c>Runtime:Correlation</c>.</summary>
public sealed record CorrelationSettings
{
    /// <summary>A related service's symptom starting within this long of an open incident's onset is merged into it.</summary>
    public TimeSpan MergeWindow { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>The same symptom returning within this long of resolution, with no new change, reopens the incident.</summary>
    public TimeSpan ReopenWindow { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Services at most this many dependency hops apart are related.</summary>
    public int MaxMergeDistance { get; init; } = 1;

    /// <summary>Changes scoring below this are not linked to a new incident.</summary>
    public double MinimumChangeScore { get; init; } = 0.2;

    /// <summary>Temporal proximity halves roughly every <c>0.7 ×</c> this interval.</summary>
    public TimeSpan ProximityScale { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>A maintenance window or known-noise rule: matching signals open nothing.</summary>
public sealed record Suppression(ServiceId? ServiceId, string? MetricName, DateTimeOffset From, DateTimeOffset Until, string Reason)
{
    public bool Matches(ServiceId service, string metric, DateTimeOffset at) =>
        (ServiceId is null || ServiceId == service) && (MetricName is null || MetricName == metric) && at >= From && at < Until;
}

/// <summary>An incident as correlation sees it: who it affects and when it began or ended.</summary>
public sealed record IncidentContext(
    IncidentId IncidentId,
    ServiceId AffectedServiceId,
    IReadOnlyList<ServiceId> AffectedServices,
    DateTimeOffset SymptomOnset,
    bool IsOpen,
    DateTimeOffset? ResolvedAt);

/// <summary>A firing detection signal.</summary>
public sealed record CorrelationSignal(ServiceId ServiceId, string MetricName, double Value, DateTimeOffset Onset, DateTimeOffset EventTime, ObservationId ObservationId);

/// <summary>
/// Dependency edges between services, from the service catalog and from observed traces. Distance
/// is undirected: a symptom can travel either way along a call edge.
/// </summary>
public sealed class ServiceTopology
{
    private readonly Dictionary<ServiceId, HashSet<ServiceId>> _neighbours = [];

    public ServiceTopology(IEnumerable<(ServiceId Caller, ServiceId Callee)> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);

        foreach ((ServiceId caller, ServiceId callee) in edges)
        {
            Neighbours(caller).Add(callee);
            Neighbours(callee).Add(caller);
        }
    }

    public static ServiceTopology Empty { get; } = new([]);

    /// <summary>Hops between two services, or null when they are not connected within <paramref name="maximum"/>.</summary>
    public int? Distance(ServiceId from, ServiceId to, int maximum = 3)
    {
        if (from == to)
        {
            return 0;
        }

        var seen = new HashSet<ServiceId> { from };
        var frontier = new List<ServiceId> { from };
        for (int hops = 1; hops <= maximum && frontier.Count > 0; hops++)
        {
            var next = new List<ServiceId>();
            foreach (ServiceId neighbour in frontier.SelectMany(service => _neighbours.TryGetValue(service, out HashSet<ServiceId>? set) ? set : []))
            {
                if (neighbour == to)
                {
                    return hops;
                }

                if (seen.Add(neighbour))
                {
                    next.Add(neighbour);
                }
            }

            frontier = next;
        }

        return null;
    }

    private HashSet<ServiceId> Neighbours(ServiceId service)
    {
        if (!_neighbours.TryGetValue(service, out HashSet<ServiceId>? set))
        {
            set = [];
            _neighbours[service] = set;
        }

        return set;
    }
}

public abstract record CorrelationDecision(IReadOnlyList<string> Reasons);

/// <summary>A new incident, with the changes worth investigating and why each was linked.</summary>
public sealed record OpenIncident(IReadOnlyList<LinkedChange> Changes, IReadOnlyList<string> Reasons) : CorrelationDecision(Reasons);

/// <summary>The signal is another symptom of an open incident on a related service (blast radius).</summary>
public sealed record MergeIntoIncident(IncidentId IncidentId, double Score, IReadOnlyList<string> Reasons) : CorrelationDecision(Reasons);

/// <summary>The service already has an open incident; the sample is absorbed.</summary>
public sealed record AbsorbIntoIncident(IncidentId IncidentId, IReadOnlyList<string> Reasons) : CorrelationDecision(Reasons);

public sealed record ReopenIncident(IncidentId IncidentId, IReadOnlyList<string> Reasons) : CorrelationDecision(Reasons);

public sealed record SuppressSignal(IReadOnlyList<string> Reasons) : CorrelationDecision(Reasons);

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

/// <summary>
/// Severity from SLO impact and blast radius (Phase 03): the burn rate sets the base level, using the
/// multi-window burn-rate convention (14.4 pages at once, 6 within the hour), and an incident
/// affecting three or more services is raised one level.
/// </summary>
public static class SeverityPolicy
{
    public static IncidentSeverity Assess(double burnRate, int affectedServices)
    {
        IncidentSeverity level = burnRate switch
        {
            >= 14.4 => IncidentSeverity.Critical,
            >= 6 => IncidentSeverity.High,
            >= 2 => IncidentSeverity.Medium,
            _ => IncidentSeverity.Low
        };

        return affectedServices >= 3 && level != IncidentSeverity.Critical ? level - 1 : level;
    }
}
