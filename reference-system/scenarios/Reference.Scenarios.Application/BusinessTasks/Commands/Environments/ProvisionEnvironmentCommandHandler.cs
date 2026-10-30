using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Abstractions;

namespace Reference.Scenarios.Application.BusinessTasks.Commands.Environments;

public sealed class ProvisionEnvironmentCommandHandler(IReferenceEnvironment environment) : ICommandHandler<ProvisionEnvironmentCommand>
{
    public async Task<Result> Handle(ProvisionEnvironmentCommand command, CancellationToken cancellationToken)
    {
        await environment.ProvisionAsync(cancellationToken);
        return Result.Success();
    }
}
