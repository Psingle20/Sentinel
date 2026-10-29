using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

/// <summary>
/// The same symptom returned soon after the incident was resolved, with no new change to explain
/// it: the incident reopens and its investigation cycle starts again. Earlier cycles stay in the stream.
/// </summary>
public sealed record IncidentReopened(string Reason, IReadOnlyList<ObservationId> TriggerObservationIds, DateTimeOffset SymptomOnset, DateTimeOffset OccurredAt) : IIncidentEvent;
