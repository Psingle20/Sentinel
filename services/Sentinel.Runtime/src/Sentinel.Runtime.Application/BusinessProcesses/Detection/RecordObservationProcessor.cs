using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Detection;
using Sentinel.Contracts.Observations;

namespace Sentinel.Runtime.Application.BusinessProcesses.Detection;

public sealed class RecordObservationProcessor(ICommandDispatcher commands) : IProcessor<RecordObservationRequest>
{
    public Task<Result> Process(RecordObservationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new RecordObservationCommand(request.Observation), cancellationToken);
    }
}
