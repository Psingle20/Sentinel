using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Observations;

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
