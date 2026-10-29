using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Application.Memory;

/// <summary>
/// The features incidents are compared on (Phase 07). Observable features are known when an
/// investigation starts: affected services, symptoms and changes. Outcome features are known only
/// once the incident closed: the cause, the culprit, the action and the outcome.
/// </summary>
public static class MemoryFeatures
{
    private static readonly string[] ObservablePrefixes = ["service:", "symptom:", "change-service:", "change-kind:"];

    private static readonly Dictionary<string, double> Weights = new(StringComparer.Ordinal)
    {
        ["service:"] = 1,
        ["symptom:"] = 2,
        ["change-service:"] = 1.5,
        ["change-kind:"] = 0.5,
        ["cause:"] = 3,
        ["culprit:"] = 2,
        ["action:"] = 1,
        ["outcome:"] = 0.5
    };

    public static IReadOnlyList<string> Observable(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var features = new SortedSet<string>(StringComparer.Ordinal);
        foreach (ServiceId service in incident.AffectedServices)
        {
            features.Add($"service:{service}");
        }

        if (incident.TriggerMetric is { } metric)
        {
            features.Add($"symptom:{incident.AffectedServiceId}/{metric}");
        }

        foreach (SignalLinked signal in incident.LinkedSignals)
        {
            features.Add($"symptom:{signal.ServiceId}/{signal.MetricName}");
        }

        foreach (LinkedChange change in incident.Changes)
        {
            features.Add($"change-service:{change.ServiceId}");
            features.Add($"change-kind:{change.ChangeKind}");
        }

        return [.. features];
    }

    public static IReadOnlyList<string> All(Incident incident, IncidentSnapshot snapshot, MemoryAction? action, IncidentStatus status)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var features = new SortedSet<string>(Observable(incident), StringComparer.Ordinal);
        if (snapshot.Decision == DecisionResult.Diagnosed && snapshot.LeadingKey is { } key)
        {
            features.Add($"cause:{key}");
        }

        if (snapshot.Decision == DecisionResult.Diagnosed && snapshot.LeadingServiceId is { } culprit)
        {
            features.Add($"culprit:{culprit}");
        }

        if (action is not null)
        {
            features.Add($"action:{action.Kind}:{action.TargetServiceId}");
        }

        features.Add($"outcome:{status.ToString().ToLowerInvariant()}");
        return [.. features];
    }

    public static bool IsObservable(string feature) => ObservablePrefixes.Any(prefix => feature.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Weighted Jaccard similarity of two feature sets, optionally over observable features only.</summary>
    public static (double Score, IReadOnlyList<string> Shared) Score(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right, bool observableOnly)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        HashSet<string> a = [.. left.Where(feature => !observableOnly || IsObservable(feature))];
        HashSet<string> b = [.. right.Where(feature => !observableOnly || IsObservable(feature))];
        string[] shared = [.. a.Intersect(b).Order(StringComparer.Ordinal)];
        double union = a.Union(b).Sum(Weight);
        return union <= 0 ? (0, []) : (Math.Round(shared.Sum(Weight) / union, 4), shared);
    }

    private static double Weight(string feature)
    {
        foreach ((string prefix, double weight) in Weights)
        {
            if (feature.StartsWith(prefix, StringComparison.Ordinal))
            {
                return weight;
            }
        }

        return 1;
    }
}
