using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

/// <summary>The Gateway relayed an approver's signed approval of a plan version (ADR-006).</summary>
public sealed record RecordApprovalRequest(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string PlanHash, string ApproverId, string Attestation) : IRequest;