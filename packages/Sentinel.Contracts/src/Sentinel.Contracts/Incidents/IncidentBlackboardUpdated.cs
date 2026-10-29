using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// Runtime accepted an investigation's findings onto the incident's evidence blackboard (Phase 04).
/// A notification for live views: it carries counts and keys, and the detail is read from Runtime.
/// </summary>
public sealed record IncidentBlackboardUpdated : ISentinelMessage
{
    public static string MessageType => "runtime.incident.blackboard-updated";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Incidents;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required InvestigationId InvestigationId { get; init; }

    public string? Role { get; init; }

    public required int AcceptedEvidence { get; init; }

    public required int RejectedEvidence { get; init; }

    /// <summary>The blackboard's hypothesis keys after the merge.</summary>
    public IReadOnlyList<string> HypothesisKeys { get; init; } = [];

    public required DateTimeOffset UpdatedAt { get; init; }

    public required long StreamVersion { get; init; }
}
