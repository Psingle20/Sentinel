using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

public sealed record RemediationExecuted : ISentinelMessage
{
    public static string MessageType => "executor.remediation.executed";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Remediation;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required ExecutionOutcome Outcome { get; init; }

    public string? Detail { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }
}
