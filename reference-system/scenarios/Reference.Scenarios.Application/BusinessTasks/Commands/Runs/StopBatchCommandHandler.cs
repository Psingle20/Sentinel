using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Abstractions;

namespace Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

public sealed class StopBatchCommandHandler(IReferenceControl reference) : ICommandHandler<StopBatchCommand>
{
    public async Task<Result> Handle(StopBatchCommand command, CancellationToken cancellationToken)
    {
        await reference.StopBatchAsync(cancellationToken);
        return Result.Success();
    }
}
