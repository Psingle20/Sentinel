using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>Api relays an approval with its signed attestation; Runtime cannot create one (ADR-006).</summary>
public sealed record RecordApproval : ISentinelMessage
{
    public static string MessageType => "runtime.record-approval";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.RuntimeCommands;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string PlanHash { get; init; }

    /// <summary>Compact JWS signed by Api's approval key.</summary>
    public required string Attestation { get; init; }

    public required string ApproverId { get; init; }

    public required DateTimeOffset ApprovedAt { get; init; }
}
