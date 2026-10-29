using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>Api relays a rejection of a plan version to Runtime, signed so Runtime can verify who decided (ADR-006).</summary>
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

    /// <summary>Compact JWS signed by the Gateway with the reject-plan scope. Absent only in messages written before the signing of rejections; Runtime refuses those.</summary>
    public string? Attestation { get; init; }
}
