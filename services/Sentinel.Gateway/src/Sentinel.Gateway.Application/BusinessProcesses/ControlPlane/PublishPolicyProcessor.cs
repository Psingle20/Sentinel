using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;
using Sentinel.Gateway.Application.Abstractions.Providers;
using System.Text.Json;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class PublishPolicyProcessor(ICommandDispatcher commands) : IProcessor<PublishPolicyRequest, ControlPlaneResponse>
{
    public Task<Result<ControlPlaneResponse>> Process(PublishPolicyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new PublishPolicyCommand(request.Document, request.ExpectedVersion, request.Actor), cancellationToken);
    }
}
