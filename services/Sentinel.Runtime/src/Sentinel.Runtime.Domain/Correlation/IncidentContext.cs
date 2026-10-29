using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation;

/// <summary>An incident as correlation sees it: who it affects and when it began or ended.</summary>
public sealed record IncidentContext(
    IncidentId IncidentId,
    ServiceId AffectedServiceId,
    IReadOnlyList<ServiceId> AffectedServices,
    DateTimeOffset SymptomOnset,
    bool IsOpen,
    DateTimeOffset? ResolvedAt);
