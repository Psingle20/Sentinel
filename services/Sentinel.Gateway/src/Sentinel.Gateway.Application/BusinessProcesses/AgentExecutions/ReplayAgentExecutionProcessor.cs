using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Commands.AgentExecutions;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.AgentExecutions;

public sealed class ReplayAgentExecutionProcessor(ICommandDispatcher commands) : IProcessor<ReplayAgentExecutionRequest, AgentReplayResponse?>
{
    public Task<Result<AgentReplayResponse?>> Process(ReplayAgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new ReplayAgentExecutionCommand(request.ExecutionId, request.Replay, request.Tenants), cancellationToken);
    }
}
