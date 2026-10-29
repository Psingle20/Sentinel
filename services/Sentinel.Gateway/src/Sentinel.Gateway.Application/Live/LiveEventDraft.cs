using System.Globalization;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Application.Live;

/// <summary>A live event before the store assigns its sequence.</summary>
public sealed record LiveEventDraft(
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    IncidentId IncidentId,
    string Kind,
    long StreamVersion,
    string Summary,
    Severity? Severity,
    DateTimeOffset OccurredAt);
