using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Detection;

/// <summary>Hands the observation to its correlation partition, where detection runs serialized.</summary>
public sealed class RecordObservationCommandHandler(ICorrelationPartitions partitions) : ICommandHandler<RecordObservationCommand>
{
    public async Task<Result> Handle(RecordObservationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await partitions.ObserveAsync(command.Observation, cancellationToken);
        return Result.Success();
    }
}
