using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;

namespace Sentinel.Gateway.Application.Commands.Approvals;

public sealed record RejectPlanCommand(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string Reason, string ApproverId) : ICommand<ApprovalReceipt>;