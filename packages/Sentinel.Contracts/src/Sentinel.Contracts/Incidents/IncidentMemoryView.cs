using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// One incident remembered (Phase 07): its symptoms, topology, changes, evidence, hypotheses,
/// decision, actions and outcome, linked by ID to the authoritative stream it was derived from.
/// Purged memory keeps only its identity and retention metadata.
/// </summary>
public sealed record IncidentMemoryView
{
    public required IncidentId IncidentId { get; init; }

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required string Title { get; init; }

    public required IncidentStatus Status { get; init; }

    public required DateTimeOffset OpenedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    public required int Cycle { get; init; }

    /// <summary>The stream version the record was derived from.</summary>
    public required long StreamVersion { get; init; }

    public IReadOnlyList<ServiceId> AffectedServices { get; init; } = [];

    public IReadOnlyList<SymptomSignature> Symptoms { get; init; } = [];

    public IReadOnlyList<LinkedChangeView> Changes { get; init; } = [];

    public IReadOnlyList<string> HypothesisKeys { get; init; } = [];

    public IReadOnlyList<EvidenceId> EvidenceIds { get; init; } = [];

    public IReadOnlyList<ObservationId> ObservationIds { get; init; } = [];

    public DecisionOutcome? Decision { get; init; }

    public MemoryCause? Cause { get; init; }

    public MemoryAction? Action { get; init; }

    public required VersionManifest Versions { get; init; }

    public required RetentionMetadata Retention { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>The features similar-incident retrieval matches on, for example <c>service:checkout-service</c>.</summary>
    public IReadOnlyList<string> Features { get; init; } = [];
}
