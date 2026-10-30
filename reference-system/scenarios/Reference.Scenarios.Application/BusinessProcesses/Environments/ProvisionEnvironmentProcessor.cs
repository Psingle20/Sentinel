using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessTasks.Commands.Environments;

namespace Reference.Scenarios.Application.BusinessProcesses.Environments;

public sealed class ProvisionEnvironmentProcessor(ICommandDispatcher commands) : IProcessor<ProvisionEnvironmentRequest>
{
    public Task<Result> Process(ProvisionEnvironmentRequest request, CancellationToken cancellationToken)
    {
        return commands.Dispatch(new ProvisionEnvironmentCommand(), cancellationToken);
    }
}
