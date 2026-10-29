using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>
/// The Gateway asks Runtime to withdraw an approval (Phase 06), as a bounded request whose answer is
/// <see cref="RevocationResponse"/>: Runtime is the authority on whether execution already started, so
/// the Gateway records a revocation only once Runtime accepted it (ADR-006). Carried over HTTP; the
/// message form stays registered for the stream name it once used.
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

    /// <summary>Compact JWS signed by the Gateway with the revoke-approval scope; Runtime refuses a revocation without one.</summary>
    public string? Attestation { get; init; }
}
