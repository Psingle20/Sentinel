using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Configuration;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Application.BusinessProcesses.Configuration;

public sealed class PublishRuntimeConfigurationProcessor(ICommandDispatcher commands) : IProcessor<PublishRuntimeConfigurationRequest, DocumentVersion>
{
    public Task<Result<DocumentVersion>> Process(PublishRuntimeConfigurationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new PublishRuntimeConfigurationCommand(request.Document, request.ExpectedVersion, request.Author), cancellationToken);
    }
}
