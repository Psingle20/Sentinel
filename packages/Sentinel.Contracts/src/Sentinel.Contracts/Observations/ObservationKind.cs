using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Observations;

public enum ObservationKind
{
    Metric,
    Log,
    Trace,
    Deployment,
    Topology,
    Alert
}
