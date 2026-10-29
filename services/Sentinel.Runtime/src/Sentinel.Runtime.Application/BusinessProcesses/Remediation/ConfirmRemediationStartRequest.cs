using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

/// <summary>Executor asks to start a plan, immediately before any side effect (ADR-006); refusal is the safe answer.</summary>
public sealed record ConfirmRemediationStartRequest(TenantId TenantId, EnvironmentId EnvironmentId, IncidentId IncidentId, PlanId PlanId, int PlanVersion, long FencingToken) : IRequest<RemediationStartResponse>;
