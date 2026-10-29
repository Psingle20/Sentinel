using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// Runtime's incident read model, served to Api over HTTP (ADR-012 bounded request/response).
/// Views are projections: rebuildable from the incident stream and never authoritative.
/// </summary>
public sealed record IncidentSummary
{
    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required ServiceId AffectedServiceId { get; init; }

    public required string Title { get; init; }

    public required Severity Severity { get; init; }

    public required IncidentStatus Status { get; init; }

    public required DateTimeOffset OpenedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required long StreamVersion { get; init; }

    /// <summary>When symptoms began in event time; the incident opened shortly after.</summary>
    public DateTimeOffset? SymptomOnset { get; init; }
}
