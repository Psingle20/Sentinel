using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Observations;

/// <summary>
/// A canonical, vendor-neutral observation (architecture §7). Provider payloads are translated
/// into these shapes inside Sentinel.Ingestion; nothing downstream sees a vendor format.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MetricObservation), "metric")]
[JsonDerivedType(typeof(LogObservation), "log")]
[JsonDerivedType(typeof(TraceObservation), "trace")]
[JsonDerivedType(typeof(DeploymentObservation), "deployment")]
[JsonDerivedType(typeof(TopologyObservation), "topology")]
[JsonDerivedType(typeof(AlertObservation), "alert")]
public abstract record Observation
{
    public required ObservationId ObservationId { get; init; }

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required ServiceId ServiceId { get; init; }

    /// <summary>The provider the observation came from, for example <c>prometheus</c>.</summary>
    public required string SourceProvider { get; init; }

    /// <summary>The provider resource, for example a metric series or log stream, for provenance.</summary>
    public required string SourceResource { get; init; }

    public required DateTimeOffset EventTime { get; init; }

    public required DateTimeOffset IngestTime { get; init; }

    public TraceContext? TraceContext { get; init; }

    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
}
