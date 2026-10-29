using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// A remembered incident that resembled this one when an investigation was requested (Phase 07).
/// Kept in the request event so the investigation's inputs are part of the stream and replayable.
/// </summary>
public sealed record PriorIncident(IncidentId IncidentId, double Similarity, IReadOnlyList<string> SharedFeatures, string? CauseKey, string? CauseStatement, string Outcome, DateTimeOffset RecordedAt);
