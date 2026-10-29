using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Abstractions;

namespace Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

public sealed class ResetReferenceCommandHandler(IReferenceControl reference) : ICommandHandler<ResetReferenceCommand>
{
    public async Task<Result> Handle(ResetReferenceCommand command, CancellationToken cancellationToken)
    {
        await reference.ResetAsync(cancellationToken);
        return Result.Success();
    }
}
