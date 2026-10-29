using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Approvals;

/// <summary>An approver approves the plan version, with the content hash, they reviewed.</summary>
public sealed record ApprovePlanRequest(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string PlanHash, string ApproverId) : IRequest<ApprovalReceipt>;
