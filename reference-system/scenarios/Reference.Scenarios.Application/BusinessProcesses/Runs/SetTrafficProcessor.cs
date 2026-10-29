using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessTasks.Commands.Runs;
using Reference.Scenarios.Application.Traffic;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

public sealed class SetTrafficProcessor(ICommandDispatcher commands) : IProcessor<SetTrafficRequest, TrafficSettings>
{
    public Task<Result<TrafficSettings>> Process(SetTrafficRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new SetTrafficCommand(request.RequestsPerSecond, request.Paused), cancellationToken);
    }
}
