using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

/// <summary>
/// The incident's investigation cycle starts again; earlier cycles stay in the stream. Either the same symptom
/// returned soon after the incident was resolved, with no new change to explain it, or, when
/// <see cref="FollowsPlanId"/> is set, a remedy took effect at its target but the incident did not recover, and
/// Sentinel investigates once more knowing what was tried (Phase E03).
/// </summary>
public sealed record IncidentReopened(string Reason, IReadOnlyList<ObservationId> TriggerObservationIds, DateTimeOffset SymptomOnset, DateTimeOffset OccurredAt, PlanId? FollowsPlanId = null) : IIncidentEvent;
