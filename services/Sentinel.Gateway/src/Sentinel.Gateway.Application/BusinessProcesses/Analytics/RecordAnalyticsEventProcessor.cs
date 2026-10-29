using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Commands.Analytics;
using Sentinel.Gateway.Application.Analytics;

namespace Sentinel.Gateway.Application.BusinessProcesses.Analytics;

public sealed class RecordAnalyticsEventProcessor(ICommandDispatcher commands) : IProcessor<RecordAnalyticsEventRequest>
{
    public Task<Result> Process(RecordAnalyticsEventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new RecordAnalyticsEventCommand(request.Event), cancellationToken);
    }
}
