using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Commands.Postmortems;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Postmortems;

public sealed class SaveNarrativeProcessor(ICommandDispatcher commands) : IProcessor<SaveNarrativeRequest, PostmortemNarrative>
{
    public Task<Result<PostmortemNarrative>> Process(SaveNarrativeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new SaveNarrativeCommand(request.IncidentId, request.Markdown, request.ExpectedVersion, request.EditorId), cancellationToken);
    }
}
