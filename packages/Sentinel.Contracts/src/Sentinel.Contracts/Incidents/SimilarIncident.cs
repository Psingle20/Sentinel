using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// A remembered incident that resembles another (Phase 07), with the features they share as
/// provenance. A hint for investigators, never evidence: it cites no observation of this incident.
/// <see cref="ActionSummary"/> names what was done about the remembered incident, when anything was (Phase E03).
/// </summary>
public sealed record SimilarIncident(
    IncidentId IncidentId,
    double Similarity,
    IReadOnlyList<string> SharedFeatures,
    string? CauseKey,
    string? CauseStatement,
    string Outcome,
    DateTimeOffset RecordedAt,
    string? ActionSummary = null);
