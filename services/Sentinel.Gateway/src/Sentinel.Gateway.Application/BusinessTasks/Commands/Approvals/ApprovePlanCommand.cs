using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Approvals;

public sealed record ApprovePlanCommand(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string PlanHash, string ApproverId) : ICommand<ApprovalReceipt>;