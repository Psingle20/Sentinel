using System.Globalization;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Application.Live;

/// <summary>
/// A change to an incident, published to responders (Phase 08 live updates). It says what changed
/// and at which stream version; the query API stays the source of truth, so a client that receives
/// one refetches the incident rather than patching its copy.
/// </summary>
public sealed record LiveEvent(
    long Sequence,
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    IncidentId IncidentId,
    string Kind,
    long StreamVersion,
    string Summary,
    Severity? Severity,
    DateTimeOffset OccurredAt);
