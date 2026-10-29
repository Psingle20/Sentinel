using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>
/// Runtime asks Executor to run an approved plan. <see cref="FencingToken"/> is the incident
/// stream version the command was based on (ADR-004, ADR-006).
/// </summary>
public sealed record ExecuteRemediation : ISentinelMessage
{
    public static string MessageType => "executor.execute-remediation";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.ExecutorCommands;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required RemediationPlan Plan { get; init; }

    public required string Attestation { get; init; }

    public required long FencingToken { get; init; }

    public required string IdempotencyKey { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }
}
