using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// Runtime recorded an evidence-backed decision. Confidence is always published with its
/// evidence completeness and calibration (Phase 05).
/// </summary>
public sealed record DecisionRecorded : ISentinelMessage
{
    public static string MessageType => "runtime.decision.recorded";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Incidents;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required DecisionId DecisionId { get; init; }

    public required DecisionOutcome Outcome { get; init; }

    public HypothesisId? LeadingHypothesisId { get; init; }

    public IReadOnlyList<HypothesisId> RejectedHypothesisIds { get; init; } = [];

    public required double Confidence { get; init; }

    public required double EvidenceCompleteness { get; init; }

    public required Calibration Calibration { get; init; }

    public required string Rationale { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }

    public required long StreamVersion { get; init; }
}
