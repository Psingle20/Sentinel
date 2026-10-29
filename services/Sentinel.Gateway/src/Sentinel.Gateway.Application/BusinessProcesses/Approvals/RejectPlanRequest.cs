using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Approvals;

/// <summary>An approver rejects a plan version, with the reason.</summary>
public sealed record RejectPlanRequest(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string Reason, string ApproverId) : IRequest<ApprovalReceipt>;
