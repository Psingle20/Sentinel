using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessTasks.Commands.Deployments;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

public sealed class CompleteRolloutProcessor(ICommandDispatcher commands) : IProcessor<CompleteRolloutRequest>
{
    public Task<Result> Process(CompleteRolloutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new CompleteRolloutCommand(request.DeploymentId), cancellationToken);
    }
}
