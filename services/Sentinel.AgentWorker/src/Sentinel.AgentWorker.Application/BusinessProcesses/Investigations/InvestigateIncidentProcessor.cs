using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.BusinessTasks.Commands.Investigations;
using Sentinel.Contracts.Investigations;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Investigations;

public sealed class InvestigateIncidentProcessor(ICommandDispatcher commands) : IProcessor<InvestigateIncidentRequest>
{
    public Task<Result> Process(InvestigateIncidentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new InvestigateIncidentCommand(request.Message), cancellationToken);
    }
}
