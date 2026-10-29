using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.BusinessTasks.Commands.Policy;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessProcesses.Policy;

public sealed class PublishPolicyProcessor(ICommandDispatcher commands) : IProcessor<PublishPolicyRequest, PolicyDocument>
{
    public Task<Result<PolicyDocument>> Process(PublishPolicyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new PublishPolicyCommand(request.Document, request.Author, request.ExpectedVersion), cancellationToken);
    }
}
