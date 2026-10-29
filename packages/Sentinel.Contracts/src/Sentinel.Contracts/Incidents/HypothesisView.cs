using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// A blackboard hypothesis: one explanation however many investigators proposed it. Lineage lists
/// the merged proposals; a refinement names the broader explanation it narrows.
/// </summary>
public sealed record HypothesisView(
    HypothesisFinding Finding,
    InvestigationId InvestigationId,
    HypothesisStatus Status,
    IReadOnlyList<HypothesisId>? Lineage = null,
    IReadOnlyList<InvestigationId>? ProposedBy = null,
    string? Refines = null);
