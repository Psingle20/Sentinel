using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.Postmortems;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Postmortems;

/// <summary>Saves a narrative over the version it was based on; a stale edit is refused rather than overwriting another.</summary>
public sealed class SaveNarrativeCommandHandler(IRuntimeHistory history, INarrativeStore narratives, IUnitOfWork unitOfWork, TimeProvider time) : ICommandHandler<SaveNarrativeCommand, PostmortemNarrative>
{
    public async Task<Result<PostmortemNarrative>> Handle(SaveNarrativeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Markdown.Length > PostmortemErrors.MaxNarrativeLength)
        {
            return Result.Failure<PostmortemNarrative>(PostmortemErrors.TooLong);
        }

        if (await history.PostmortemAsync(command.IncidentId, cancellationToken) is null)
        {
            return Result.Failure<PostmortemNarrative>(PostmortemErrors.NotFound);
        }

        var narrative = new PostmortemNarrative(command.Markdown, command.ExpectedVersion + 1, command.EditorId, time.GetUtcNow());
        if (!await narratives.SaveAsync(command.IncidentId, narrative, command.ExpectedVersion, cancellationToken))
        {
            return Result.Failure<PostmortemNarrative>(PostmortemErrors.VersionConflict);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success(narrative);
    }
}
