using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.BusinessTasks.Commands.Configuration;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Configuration;

public sealed class PublishSourceConfigurationProcessor(ICommandDispatcher commands) : IProcessor<PublishSourceConfigurationRequest, DocumentVersion>
{
    public Task<Result<DocumentVersion>> Process(PublishSourceConfigurationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new PublishSourceConfigurationCommand(request.Document, request.ExpectedVersion, request.Author), cancellationToken);
    }
}
