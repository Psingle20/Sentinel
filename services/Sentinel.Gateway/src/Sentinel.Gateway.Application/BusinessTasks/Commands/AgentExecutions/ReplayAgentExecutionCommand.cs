using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.AgentExecutions;

public sealed record ReplayAgentExecutionCommand(AgentExecutionId ExecutionId, InvestigationReplayRequest Replay, IReadOnlyList<TenantId> Tenants) : ICommand<AgentReplayResponse?>;
