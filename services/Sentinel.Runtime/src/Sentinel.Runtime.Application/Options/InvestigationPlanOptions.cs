using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Options;

/// <summary>Which investigators Runtime assigns (Phase 04) and whether hypotheses are challenged (Phase 05).</summary>
public sealed class InvestigationPlanOptions
{
    /// <summary>Roles investigated in parallel for every incident; <c>generalist</c> is the walking skeleton's single investigator.</summary>
    public IList<string> Roles { get; } = [];

    /// <summary>The configured roles, or the generalist alone when none are configured.</summary>
    public IReadOnlyList<string> EffectiveRoles => Roles.Count == 0 ? [InvestigationRoles.Generalist] : [.. Roles.Distinct(StringComparer.Ordinal)];

    /// <summary>Runs the adversarial verifier on the hypotheses before deciding.</summary>
    public bool Challenge { get; set; }

    /// <summary>The escalation investigator after an ambiguous consensus (Phase E05).</summary>
    public EscalationOptions Escalation { get; set; } = new();

    /// <summary>How long the workflow waits beyond an investigation's own budget before giving up on it.</summary>
    public TimeSpan ReportGrace { get; set; } = TimeSpan.FromSeconds(30);
}
