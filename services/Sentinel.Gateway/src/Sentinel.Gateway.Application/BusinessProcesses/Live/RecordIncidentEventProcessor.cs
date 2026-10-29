using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Commands.Live;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Application.BusinessProcesses.Live;

public sealed class RecordIncidentEventProcessor(ICommandDispatcher commands) : IProcessor<RecordIncidentEventRequest>
{
    public Task<Result> Process(RecordIncidentEventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new RecordIncidentEventCommand(request.Draft), cancellationToken);
    }
}
