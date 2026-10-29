using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

public sealed record RecordExecutionResultCommand(IncidentId IncidentId, PlanId PlanId, int PlanVersion, ExecutionResult Result, string? Detail) : ICommand;
