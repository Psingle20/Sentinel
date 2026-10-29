using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>Runtime proposed a plan, with Executor's advisory plan-time policy evaluation (ADR-006).</summary>
public sealed record RemediationPlanProposed : ISentinelMessage
{
    public static string MessageType => "runtime.remediation-plan.proposed";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Incidents;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required RemediationPlan Plan { get; init; }

    public required PolicyEvaluation PolicyEvaluation { get; init; }

    public required long StreamVersion { get; init; }
}
