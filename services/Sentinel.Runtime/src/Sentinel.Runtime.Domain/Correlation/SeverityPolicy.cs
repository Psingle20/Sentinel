using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation;

/// <summary>
/// Severity from SLO impact and blast radius (Phase 03): the burn rate sets the base level, using the
/// multi-window burn-rate convention (14.4 pages at once, 6 within the hour), and an incident
/// affecting three or more services is raised one level.
/// </summary>
public static class SeverityPolicy
{
    public static IncidentSeverity Assess(double burnRate, int affectedServices)
    {
        IncidentSeverity level = burnRate switch
        {
            >= 14.4 => IncidentSeverity.Critical,
            >= 6 => IncidentSeverity.High,
            >= 2 => IncidentSeverity.Medium,
            _ => IncidentSeverity.Low
        };

        return affectedServices >= 3 && level != IncidentSeverity.Critical ? level - 1 : level;
    }
}
