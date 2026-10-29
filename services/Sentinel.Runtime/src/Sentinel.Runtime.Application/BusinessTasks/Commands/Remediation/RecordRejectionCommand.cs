using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

public sealed record RecordRejectionCommand(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string RejectedBy, string Reason) : ICommand;