using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>
/// Api relays an approver's revocation of an approval to Runtime (Phase 06). Runtime refuses the
/// start of any execution of the revoked plan, so a revoked attestation can never take effect.
/// </summary>
public sealed record RecordRevocation : ISentinelMessage
{
    public static string MessageType => "runtime.record-revocation";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.RuntimeCommands;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string RevokedBy { get; init; }

    public required string Reason { get; init; }

    public required DateTimeOffset RevokedAt { get; init; }
}
