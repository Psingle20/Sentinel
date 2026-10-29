using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

public sealed record EvidenceView(EvidenceFinding Finding, InvestigationId InvestigationId, EvidenceStatus Status, string? RejectionReason);
