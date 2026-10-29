using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation;

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
