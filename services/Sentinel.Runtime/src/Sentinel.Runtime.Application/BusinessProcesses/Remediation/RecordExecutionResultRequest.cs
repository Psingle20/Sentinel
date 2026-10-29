using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

/// <summary>Executor reported how an execution ended.</summary>
public sealed record RecordExecutionResultRequest(IncidentId IncidentId, PlanId PlanId, int PlanVersion, ExecutionResult Result, string? Detail) : IRequest;
