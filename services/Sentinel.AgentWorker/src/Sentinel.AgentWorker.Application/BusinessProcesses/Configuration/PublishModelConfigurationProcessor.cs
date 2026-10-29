using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.BusinessTasks.Commands.Configuration;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Configuration;

public sealed class PublishModelConfigurationProcessor(ICommandDispatcher commands) : IProcessor<PublishModelConfigurationRequest, DocumentVersion>
{
    public Task<Result<DocumentVersion>> Process(PublishModelConfigurationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new PublishModelConfigurationCommand(request.Document, request.ExpectedVersion, request.Author), cancellationToken);
    }
}
