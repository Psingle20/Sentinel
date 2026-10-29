using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

public sealed record ConfirmRemediationStartCommand(TenantId TenantId, EnvironmentId EnvironmentId, IncidentId IncidentId, PlanId PlanId, int PlanVersion, long FencingToken) : ICommand<RemediationStartResponse>;
