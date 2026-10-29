using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.AgentExecutions;

/// <summary>
/// Sends a replay to AgentWorker, but only for an execution in the caller's tenants: any other is
/// answered as unknown, so its existence is not disclosed.
/// </summary>
public sealed class ReplayAgentExecutionCommandHandler(IAgentExecutions executions) : ICommandHandler<ReplayAgentExecutionCommand, AgentReplayResponse?>
{
    public async Task<Result<AgentReplayResponse?>> Handle(ReplayAgentExecutionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        JsonElement? execution = await executions.GetAsync(command.ExecutionId, cancellationToken);
        if (execution is not { } found || !found.TryGetProperty("tenantId", out JsonElement tenant) || !command.Tenants.Contains(TenantId.From(tenant.GetString()!)))
        {
            return Result.Success<AgentReplayResponse?>(null);
        }

        return Result.Success<AgentReplayResponse?>(await executions.ReplayAsync(command.ExecutionId, command.Replay, cancellationToken));
    }
}
