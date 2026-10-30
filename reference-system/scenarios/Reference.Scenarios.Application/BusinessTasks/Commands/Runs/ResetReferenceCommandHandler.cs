using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Abstractions;

namespace Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

public sealed class ResetReferenceCommandHandler(IReferenceEnvironment environment) : ICommandHandler<ResetReferenceCommand>
{
    public async Task<Result> Handle(ResetReferenceCommand command, CancellationToken cancellationToken)
    {
        await environment.ResetAsync(cancellationToken);
        return Result.Success();
    }
}
