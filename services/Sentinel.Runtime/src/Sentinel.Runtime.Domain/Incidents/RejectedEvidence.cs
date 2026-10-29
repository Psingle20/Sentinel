using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public sealed record RejectedEvidence(EvidenceId EvidenceId, string Reason);
