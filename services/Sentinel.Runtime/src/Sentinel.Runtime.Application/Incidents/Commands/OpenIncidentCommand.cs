using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

public sealed record OpenIncidentCommand(
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    ServiceId ServiceId,
    string Title,
    IncidentSeverity Severity,
    DateTimeOffset SymptomOnset,
    IReadOnlyList<ObservationId> Triggers,
    IReadOnlyList<LinkedChange> Changes,
    string? TriggerMetric = null) : IncidentCommand;
