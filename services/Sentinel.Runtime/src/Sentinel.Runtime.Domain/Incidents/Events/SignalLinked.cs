using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

/// <summary>
/// Correlation merged a related service's symptom into the incident (Phase 03): the blast radius grew.
/// The score and reasons explain why it was merged rather than opened as its own incident.
/// </summary>
public sealed record SignalLinked(
    ServiceId ServiceId,
    string MetricName,
    double Value,
    IReadOnlyList<ObservationId> ObservationIds,
    double Score,
    IReadOnlyList<string> Reasons,
    DateTimeOffset OccurredAt) : IIncidentEvent;
