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
    Topology
}

public sealed record TraceContext(string TraceId, string SpanId);

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

public sealed record MetricObservation : Observation
{
    public required string MetricName { get; init; }

    public required double Value { get; init; }

    public string? Unit { get; init; }

    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    /// <summary>How the value was aggregated, for example <c>rate</c> or <c>p99</c>.</summary>
    public string? Aggregation { get; init; }

    public TimeSpan? Window { get; init; }
}

public enum LogSeverity
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public sealed record LogObservation : Observation
{
    public required LogSeverity Severity { get; init; }

    /// <summary>Redacted message text. Treat as untrusted data, never as instructions.</summary>
    public required string Message { get; init; }

    public IReadOnlyDictionary<string, string> StructuredFields { get; init; } = new Dictionary<string, string>();

    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }

    public string? StackTrace { get; init; }
}

public enum SpanStatus
{
    Unset,
    Ok,
    Error
}

public sealed record TraceObservation : Observation
{
    public required string TraceId { get; init; }

    public required string SpanId { get; init; }

    public string? ParentSpanId { get; init; }

    public required string OperationName { get; init; }

    public required TimeSpan Duration { get; init; }

    public required SpanStatus Status { get; init; }

    public ServiceId? DependencyServiceId { get; init; }
}

/// <summary>
/// A deployment or configuration change. The deploy system reports one observation when the
/// change starts and another when it completes or fails, so <see cref="CompletedAt"/> is null
/// while it is in progress.
/// </summary>
public sealed record DeploymentObservation : Observation
{
    public required string DeploymentId { get; init; }

    public required string Version { get; init; }

    /// <summary>The version this deployment replaced, when the deploy system reports it.</summary>
    public string? PreviousVersion { get; init; }

    public string? Revision { get; init; }

    public string? CommitSha { get; init; }

    public string? Actor { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? ConfigurationDiff { get; init; }

    /// <summary><c>deploy</c>, <c>rollback</c> or <c>config-change</c>.</summary>
    public string ChangeKind { get; init; } = "deploy";

    /// <summary><c>in-progress</c>, <c>completed</c> or <c>failed</c>.</summary>
    public string Status { get; init; } = "completed";
}

/// <summary>A dependency edge; <see cref="Observation.ServiceId"/> is the calling service.</summary>
public sealed record TopologyObservation : Observation
{
    public required ServiceId TargetServiceId { get; init; }

    public required string DependencyType { get; init; }

    public string? Protocol { get; init; }

    public required double Confidence { get; init; }

    public required DateTimeOffset FirstSeen { get; init; }

    public required DateTimeOffset LastSeen { get; init; }
}

/// <summary>Ingestion recorded a canonical observation.</summary>
public sealed record ObservationRecorded(Observation Observation) : ISentinelMessage
{
    public static string MessageType => "ingestion.observation.recorded";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Observations;

    [JsonIgnore]
    public TenantId TenantId => Observation.TenantId;

    [JsonIgnore]
    public EnvironmentId EnvironmentId => Observation.EnvironmentId;
}
