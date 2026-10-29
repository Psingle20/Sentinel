using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.AgentExecutions;

/// <summary>A replay of an agent execution at AgentWorker; nothing when the execution is not in the caller's tenants.</summary>
public sealed record ReplayAgentExecutionRequest(AgentExecutionId ExecutionId, InvestigationReplayRequest Replay, IReadOnlyList<TenantId> Tenants) : IRequest<AgentReplayResponse?>;
