using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// The incident's authoritative state from its writer beside the version its projection was built from,
/// so a caller can tell authoritative state from a lagging projection (Phase 03).
/// </summary>
public sealed record IncidentStateView(
    IncidentId IncidentId,
    IncidentStatus Status,
    long StreamVersion,
    long? ProjectionVersion,
    long? ProjectionLag,
    int Cycle,
    IReadOnlyList<InvestigationId> PendingInvestigationIds);
