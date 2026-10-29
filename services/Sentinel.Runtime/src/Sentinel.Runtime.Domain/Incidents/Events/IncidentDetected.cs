using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

public sealed record IncidentDetected(
    IncidentId IncidentId,
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    ServiceId ServiceId,
    string Title,
    IncidentSeverity Severity,
    DateTimeOffset SymptomOnset,
    IReadOnlyList<ObservationId> TriggerObservationIds,
    IReadOnlyList<LinkedChange> LinkedChanges,
    DateTimeOffset OccurredAt,
    string? TriggerMetric = null) : IIncidentEvent;
