using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Approvals;

/// <summary>An approver withdraws an approval before execution starts (Phase 06), with the reason.</summary>
public sealed record RevokeApprovalRequest(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string Reason, string ApproverId) : IRequest<ApprovalReceipt>;