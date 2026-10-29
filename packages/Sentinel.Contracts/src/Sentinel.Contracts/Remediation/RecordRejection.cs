using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

public sealed record RecordRejection : ISentinelMessage
{
    public static string MessageType => "runtime.record-rejection";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.RuntimeCommands;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string RejectedBy { get; init; }

    public required string Reason { get; init; }

    public required DateTimeOffset RejectedAt { get; init; }
}
