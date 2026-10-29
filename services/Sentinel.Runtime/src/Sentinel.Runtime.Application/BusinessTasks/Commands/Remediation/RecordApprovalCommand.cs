using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

public sealed record RecordApprovalCommand(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string PlanHash, string ApproverId, string Attestation) : ICommand;