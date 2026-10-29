using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

/// <summary>
/// The Gateway asks Runtime to withdraw an approval before execution starts (Phase 06, ADR-006). Runtime is the authority
/// on the point of no return, so the answer says whether the revocation was accepted.
/// </summary>
public sealed record RecordRevocationRequest(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string RevokedBy, string Reason, string? Attestation) : IRequest<RevocationResponse>;
