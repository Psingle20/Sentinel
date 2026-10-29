using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

public sealed record IncidentStatusChanged : ISentinelMessage
{
    public static string MessageType => "runtime.incident.status-changed";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Incidents;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required IncidentStatus Status { get; init; }

    public string? Reason { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }

    public required long StreamVersion { get; init; }
}
