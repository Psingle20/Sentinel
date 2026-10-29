using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;
using Sentinel.Gateway.Application.Abstractions.Providers;
using System.Text.Json;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class PublishConfigurationProcessor(ICommandDispatcher commands) : IProcessor<PublishConfigurationRequest, ControlPlaneResponse>
{
    public Task<Result<ControlPlaneResponse>> Process(PublishConfigurationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new PublishConfigurationCommand(request.Area, request.Document, request.ExpectedVersion, request.Actor), cancellationToken);
    }
}
