using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

public sealed record RecordRevocationCommand(IncidentId IncidentId, PlanId PlanId, int PlanVersion, string RevokedBy, string Reason) : ICommand;
