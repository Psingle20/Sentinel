using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.BusinessTasks.Commands.Replays;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Replays;

public sealed class ReplayInvestigationProcessor(ICommandDispatcher commands) : IProcessor<ReplayInvestigationRequest, InvestigationReplayResult?>
{
    public Task<Result<InvestigationReplayResult?>> Process(ReplayInvestigationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new ReplayInvestigationCommand(request.ExecutionId, request.Replay), cancellationToken);
    }
}
